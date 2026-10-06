using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.SalesOrders.Fulfillment;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>实际公开入口经Dapper验证七阶段、投影、缓冲及原状态装配；脚本不假装执行PG商业SQL。</summary>
[TestClass]
public sealed partial class PostgreSqlSalesOrderFulfillmentMigrationTests
{
    /// <summary>可空事务逐阶段透传，完整DTO字段及两销售行的采购/历史正确路由，整采购行反馈不被分摊。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_SevenStagesMapAndRoute(bool transactional)
    {
        using var f=new ReadFixture(transactional); var first=LineSample(); var second=LineSample(); second.LineNumber=2;
        var purchase=new SalesFulfillmentPurchaseDto { SalesOrderLineId=second.SalesOrderLineId,PurchaseOrderId=Guid.NewGuid(),PurchaseOrderLineId=Guid.NewGuid(),
            PurchaseLineNumber=4,PurchaseOrderNo=" PO ",Sku=" RAW ",UnitSnapshot=" KG ",LinkedQty=2.3456m,PurchaseLineQty=20.1234m,
            IsSharedPurchaseLine=true,HasProgress=true,PurchaseLineReadyQty=19.4321m,ExpectedReadyDate=Wall, SupplierReportedDate=Wall.AddHours(2) };
        var posted=new SalesFulfillmentDeliveryDto { SalesOrderLineId=first.SalesOrderLineId,PackingListId=Guid.NewGuid(),PackingListLineId=Guid.NewGuid(),
            PackingListNo="PACK",LineNumber=3,Qty=4.1234m,UnitSnapshot=" PCS ",CountsTowardDelivery=true,PostedDate=Wall };
        var reversed=new SalesFulfillmentDeliveryDto { SalesOrderLineId=second.SalesOrderLineId,PackingListId=Guid.NewGuid(),PackingListLineId=Guid.NewGuid(),
            PackingListNo="REVERSE",LineNumber=1,Qty=2,UnitSnapshot="KG",CountsTowardDelivery=false,PostedDate=null,ReversedDate=Wall.AddDays(1) };
        var header=HeaderSample(); Seed(f,header,[first,second]);
        f.Connection.Steps[4]=Rows(purchase,new SalesFulfillmentPurchaseDto { SalesOrderLineId=Guid.NewGuid() });
        f.Connection.Steps[5]=Rows(posted,reversed,new SalesFulfillmentDeliveryDto { SalesOrderLineId=Guid.NewGuid() });
        using var cts=new CancellationTokenSource(); var result=await f.Repository.GetFulfillmentAsync(header.SalesOrderId,cts.Token);
        Assert.IsNotNull(result); AssertMapped(header,result); Assert.HasCount(2,result.Lines);
        AssertMapped(first,result.Lines[0]); AssertMapped(second,result.Lines[1]);
        Assert.HasCount(0,result.Lines[0].Purchases); Assert.HasCount(1,result.Lines[1].Purchases);
        AssertMapped(purchase,result.Lines[1].Purchases.Single()); AssertMapped(posted,result.Lines[0].Deliveries.Single());
        AssertMapped(reversed,result.Lines[1].Deliveries.Single()); Assert.AreEqual("未关联",result.Lines[0].PurchaseState);
        Assert.AreEqual("已关联",result.Lines[1].PurchaseState); AssertBorrowed(f,cts.Token,7);
        var sql=f.Connection.Sqls; StringAssert.Contains(sql[0],"h.is_deleted=FALSE"); StringAssert.Contains(sql[1],"invalid_history_count");
        Assert.IsFalse(sql[2].Contains("h.is_deleted",StringComparison.Ordinal)); Assert.IsFalse(sql[3].Contains("l.is_deleted",StringComparison.Ordinal));
        StringAssert.Contains(sql[3],"demand.source_version DESC NULLS LAST"); StringAssert.Contains(sql[4],"pol.quantity AS \"PurchaseLineQty\"");
        StringAssert.Contains(sql[4],"report.sequence DESC"); StringAssert.Contains(sql[4],"get_byte(uuid_send(report.uid),10)");
        StringAssert.Contains(sql[4],"CASE WHEN net.parent_net_demand_id IS NULL"); Assert.IsFalse(sql[4].Contains("net.unit_snapshot",StringComparison.Ordinal));
        StringAssert.Contains(sql[5],"ph.posted_date NULLS FIRST"); StringAssert.Contains(sql[6],"net.source_version_snapshot IS NULL");
        using var candidate=JsonDocument.Parse((string)f.Connection.Values[1]["Candidates"]!); Assert.AreEqual(7,candidate.RootElement[0].EnumerateObject().Count());
        using var facts=JsonDocument.Parse((string)f.Connection.Values[2]["LineFacts"]!); Assert.AreEqual(9,facts.RootElement[0].EnumerateObject().Count());
        using var frozen=JsonDocument.Parse((string)f.Connection.Values[4]["FulfillmentLines"]!); Assert.AreEqual(16,frozen.RootElement[0].EnumerateObject().Count());
        Assert.IsFalse(frozen.RootElement[0].TryGetProperty("Purchases",out _));
    }

    /// <summary>先完整缓冲履约行再执行空头分支；尾采购错误不可见，但履约阶段错误仍传播。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_NullHeaderStillBuffersLines(bool failLines)
    {
        using var f=new ReadFixture(true); Seed(f,HeaderSample(),[LineSample()]); f.Connection.Steps[2]=Rows<SalesFulfillmentDetailDto>();
        var failure=new InvalidOperationException("行阶段失败"); f.Connection.Before=(stage,_)=> stage==(failLines?4:5)?Task.FromException(failure):Task.CompletedTask;
        if(failLines) Assert.AreSame(failure,await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.GetFulfillmentAsync(Guid.NewGuid())));
        else Assert.IsNull(await f.Repository.GetFulfillmentAsync(Guid.NewGuid()));
        AssertBorrowed(f,default,4);
    }

    /// <summary>同一行重复保留源ToDictionary异常，不静默合并，也不读取尾证据。</summary>
    [TestMethod]
    public async Task Offline_DuplicateLinesKeepDictionaryFailure()
    {
        using var f=new ReadFixture(true); var line=LineSample(); Seed(f,HeaderSample(),[line,line]);
        await Assert.ThrowsAsync<ArgumentException>(()=>f.Repository.GetFulfillmentAsync(Guid.NewGuid())); AssertBorrowed(f,default,4);
    }

    /// <summary>来源未知优先；活动坏Link或采购行原因次之；没有证据才是未关联。</summary>
    [TestMethod, DataRow("需核实",true,true,"未知／需核实"), DataRow("已关联",true,false,"需核实"),
     DataRow("已关联",false,true,"需核实"), DataRow("待确认",false,false,"未关联"), DataRow("已关联",false,false,"已关联")]
    public async Task Offline_SourceAndPurchaseStatePriority(string source,bool invalid,bool reason,string state)
    {
        using var f=new ReadFixture(false); var line=LineSample(); line.SourceState=source; Seed(f,HeaderSample(),[line]);
        if(source!="待确认") f.Connection.Steps[4]=Rows(new SalesFulfillmentPurchaseDto { SalesOrderLineId=line.SalesOrderLineId,ReviewReason=reason?"采购客户归属需核实":"" });
        if(invalid) f.Connection.Steps[6]=Ids(line.SalesOrderLineId);
        var actual=(await f.Repository.GetFulfillmentAsync(Guid.NewGuid()))!.Lines.Single(); Assert.AreEqual(state,actual.PurchaseState);
        Assert.AreEqual(invalid?"采购关联证据异常":"",actual.PurchaseReviewReason);
    }

    /// <summary>未知数量、来源字段和墙上时间经Dapper保留；冻结JSON不将未知变零。</summary>
    [TestMethod]
    public async Task Offline_NullFactsRemainUnknown()
    {
        using var f=new ReadFixture(false); var line=LineSample(); line.GrossDemandId=null; line.GrossDemandQty=null; line.GrossDemandVersion=null;
        line.GrossDemandUnitSnapshot=null!; line.ShippedQty=null; line.UnshippedQty=null; line.AllowedFurtherDeliveryQty=null; line.EffectiveDueDate=null;
        Seed(f,HeaderSample(),[line]); var actual=(await f.Repository.GetFulfillmentAsync(Guid.NewGuid()))!.Lines.Single();
        // 源Dapper默认忽略DBNull字符串赋值，DTO初始化空串仍保留；不全局改变ApplyNullValues。
        line.GrossDemandUnitSnapshot="";
        AssertMapped(line,actual); Assert.IsNull(actual.ShippedQty); Assert.IsNull(actual.UnshippedQty); Assert.IsNull(actual.AllowedFurtherDeliveryQty);
        using var frozen=JsonDocument.Parse((string)f.Connection.Values[4]["FulfillmentLines"]!);
        Assert.AreEqual(JsonValueKind.Null,frozen.RootElement[0].GetProperty("ShippedQty").ValueKind);
    }

    /// <summary>每个实际ADO读取边界的技术失败或等待取消直接传播，不转换业务未知或结束借用事务。</summary>
    [TestMethod]
    [DataRow(1,false)] [DataRow(2,false)] [DataRow(3,false)] [DataRow(4,false)] [DataRow(5,false)] [DataRow(6,false)] [DataRow(7,false)]
    [DataRow(1,true)] [DataRow(2,true)] [DataRow(3,true)] [DataRow(4,true)] [DataRow(5,true)] [DataRow(6,true)] [DataRow(7,true)]
    public async Task Offline_StageFailureOrCancellation(int stage,bool cancel)
    {
        using var f=new ReadFixture(true); Seed(f,HeaderSample(),[LineSample()]); using var cts=new CancellationTokenSource();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var error=new InvalidOperationException("读取失败");
        f.Connection.Before=async (current,token)=> { if(current!=stage)return; if(!cancel)throw error; entered.SetResult(); await Task.Delay(Timeout.Infinite,token); };
        var read=f.Repository.GetFulfillmentAsync(Guid.NewGuid(),cts.Token);
        if(cancel) { await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cts.Cancel(); Assert.AreEqual(cts.Token,(await Assert.ThrowsAsync<OperationCanceledException>(()=>read)).CancellationToken); }
        else Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>read)); AssertBorrowed(f,cts.Token,stage);
    }

    /// <summary>预取消由实际命令令牌传播，空Uid不新增业务早返。</summary>
    [TestMethod]
    public async Task Offline_PreCancellationAndEmptyUid()
    {
        using var f=new ReadFixture(true); Seed(f,HeaderSample(),[]);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.GetFulfillmentAsync(Guid.Empty,new CancellationToken(true)));
        Assert.AreEqual(0,f.Connection.Commands); Assert.IsNotNull(await f.Repository.GetFulfillmentAsync(Guid.Empty));
        Assert.AreEqual(Guid.Empty,f.Connection.Values[0]["Uid"]); AssertBorrowed(f,default,7);
    }

    private static readonly DateTime Wall=new(2026,10,5,9,10,11);
    /// <summary>公开头DTO完整列样本。</summary>
    private static SalesFulfillmentDetailDto HeaderSample()=>new() { SalesOrderId=Guid.NewGuid(),OrderNo=" SO ",CustomerCode=" C ",CustomerName="客户",InvoiceNo="INV",OrderDate=Wall,DeliveryDate=Wall.AddDays(1) };
    /// <summary>公开履约临时字段完整样本，状态由生产装配生成。</summary>
    private static SalesFulfillmentLineDto LineSample()=>new() { SalesOrderLineId=Guid.NewGuid(),LineNumber=1,Sku="SKU",CustomerSku=" CS ",OrderedQty=10.1234m,UnitSnapshot=" PCS ",EffectiveDueDate=Wall,
        GrossDemandId=Guid.NewGuid(),GrossDemandQty=10.1234m,GrossDemandUnitSnapshot="PCS",GrossDemandVersion=2,SourceState="已关联",ShippedQty=4.1234m,UnshippedQty=6,AllowedFurtherDeliveryQty=6 };
    /// <summary>只反射公开DTO值，不反射私有SQL；集合及装配状态在独立断言验证。</summary>
    private static void AssertMapped<T>(T expected,T actual)
    {
        foreach(var p in ScalarProperties(typeof(T)).Where(p=>p.Name is not "PurchaseState" and not "PurchaseReviewReason"))
            Assert.AreEqual(p.GetValue(expected),p.GetValue(actual),p.Name);
    }
    /// <summary>排除嵌套集合，保留真实CLR列/枚举映射。</summary>
    private static System.Reflection.PropertyInfo[] ScalarProperties(Type type)=>type.GetProperties().Where(p=>p.PropertyType==typeof(string)||!typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)).ToArray();
    /// <summary>构造真实ADO列类型和NULL，不解释商业SQL。</summary>
    private static DataTable Rows<T>(params T[] rows)
    {
        var table=new DataTable(); var properties=ScalarProperties(typeof(T));
        foreach(var p in properties) { var type=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType; table.Columns.Add(p.Name,type.IsEnum?typeof(int):type); }
        foreach(var row in rows) table.Rows.Add(properties.Select(p=> { var value=p.GetValue(row); return value is null?DBNull.Value:value.GetType().IsEnum?Convert.ToInt32(value):value; }).ToArray());
        return table;
    }
    /// <summary>异常行的源单列结果。</summary>
    private static DataTable Ids(params Guid[] ids) { var t=new DataTable(); t.Columns.Add("SalesOrderLineId",typeof(Guid)); foreach(var id in ids)t.Rows.Add(id); return t; }
    /// <summary>七阶段独立结果；候选/事实真实字段用于检验JSON冻结关系。</summary>
    private static void Seed(ReadFixture f,SalesFulfillmentDetailDto head,SalesFulfillmentLineDto[] lines)
    {
        var candidate=new DataTable(); foreach(var (name,type) in new (string,Type)[]{("Uid",typeof(Guid)),("Version",typeof(int)),("DocumentStatus",typeof(int)),("ConfirmationStatus",typeof(int)),("CustomerId",typeof(Guid)),("OrderNo",typeof(string)),("DeliveryDate",typeof(DateTime))})candidate.Columns.Add(name,type);
        candidate.Rows.Add(head.SalesOrderId,2,0,0,DBNull.Value,head.OrderNo,Wall);
        var facts=new DataTable(); foreach(var (name,type) in new (string,Type)[]{("SalesOrderId",typeof(Guid)),("SalesOrderLineId",typeof(Guid)),("LineNumber",typeof(int)),("Qty",typeof(decimal)),("UnitSnapshot",typeof(string)),("EffectiveDueDate",typeof(DateTime)),("ShippedQty",typeof(decimal)),("UnshippedQty",typeof(decimal)),("ReviewReason",typeof(string))}) facts.Columns.Add(name,type);
        foreach(var line in lines)facts.Rows.Add(head.SalesOrderId,line.SalesOrderLineId,line.LineNumber,line.OrderedQty,line.UnitSnapshot,Wall,DBNull.Value,DBNull.Value,DBNull.Value);
        f.Connection.Steps=[candidate,facts,Rows(head),Rows(lines),Rows<SalesFulfillmentPurchaseDto>(),Rows<SalesFulfillmentDeliveryDto>(),Ids()];
    }
    /// <summary>确认每一步只读并释放reader，令牌及事务身份一致，没有仓储提交/关闭/DDL。</summary>
    private static void AssertBorrowed(ReadFixture f,CancellationToken token,int count)
    {
        Assert.AreEqual(count,f.Connection.Commands); Assert.AreSame(f.Transaction,f.Session.Transaction);
        Assert.IsTrue(f.Connection.Transactions.All(t=>ReferenceEquals(t,f.Transaction))); Assert.IsTrue(f.Connection.Tokens.All(t=>t==token));
        Assert.AreEqual(0,f.Connection.EndTransactions); Assert.AreEqual(0,f.Connection.Closes); Assert.AreEqual(f.Transaction is null?0:1,f.Connection.Begins);
        Assert.IsTrue(f.Connection.Readers.All(r=>r.IsClosed));
        foreach(var sql in f.Connection.Sqls) foreach(var forbidden in new[]{"CREATE ","DROP ","FOR UPDATE","LOCK TABLE","#Candidate"})Assert.IsFalse(sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);
    }
    /// <summary>独占无网络夹具，不修改或共享旧测试的可变结果集。</summary>
    private sealed class ReadFixture : IDisposable
    {
        public ReadConnection Connection { get; } = new();
        public DbSession Session { get; }
        public DbTransaction? Transaction { get; }
        public PostgreSqlSalesOrderQueryRepository Repository { get; }
        /// <summary>构建既有上下文和调用者可空事务。</summary>
        public ReadFixture(bool transactional)
        {
            Session = new(new ReadFactory(Connection)); if (transactional) Session.Transaction = Transaction = Connection.BeginTransaction();
            var user = new FulfillmentUser(); Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        /// <summary>夹具结束才释放会话。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>记录实际命令/参数，脚本结果不解释SQL。</summary>
    private sealed class ReadConnection : DbConnection
    {
        public List<DataTable> Steps=[]; public List<string> Sqls=[]; public List<DbDataReader> Readers=[]; public List<Dictionary<string,object?>> Values=[]; public List<DbTransaction?> Transactions=[]; public List<CancellationToken> Tokens=[];

        public DbTransaction? LastTransaction; public CancellationToken Token;
        public int Commands; public int Begins; public int EndTransactions; public int Closes;
        public Func<int,CancellationToken, Task>? Before;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "script";
        public override ConnectionState State => ConnectionState.Open;
        /// <summary>没有网络打开。</summary>
        public override void Open() { }
        /// <summary>记录越权关闭。</summary>
        public override void Close() => Closes++;
        /// <summary>禁止切库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>记录调用者事务。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new ReadTransaction(this); }
        /// <summary>建立真实Dapper可执行的命令。</summary>
        protected override DbCommand CreateDbCommand() => new ReadCommand(this);
    }

    /// <summary>仅维护事务身份及结束计数。</summary>
    private sealed class ReadTransaction(ReadConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        /// <summary>记录提交。</summary>
        public override void Commit() => connection.EndTransactions++;
        /// <summary>记录回滚。</summary>
        public override void Rollback() => connection.EndTransactions++;
        /// <summary>记录释放。</summary>
        protected override void Dispose(bool disposing) { if (disposing) connection.EndTransactions++; base.Dispose(disposing); }
    }

    /// <summary>参数使用真实Npgsql容器；只读执行由本例脚本提供。</summary>
    private sealed class ReadCommand(ReadConnection connection) : DbCommand
    {
        private readonly NpgsqlCommand parameters = new();
        [AllowNull] public override string CommandText { get; set; } = "";
        public override int CommandTimeout { get; set; }
        public override CommandType CommandType { get; set; }
        public override bool DesignTimeVisible { get; set; }
        public override UpdateRowSource UpdatedRowSource { get; set; }
        protected override DbConnection? DbConnection { get; set; } = connection;
        protected override DbTransaction? DbTransaction { get; set; }
        protected override DbParameterCollection DbParameterCollection => parameters.Parameters;
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        /// <summary>取消由传入令牌控制。</summary>
        public override void Cancel() { }
        /// <summary>无需提供者准备。</summary>
        public override void Prepare() { }
        /// <summary>禁止写入。</summary>
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <summary>本切片没有标量调用。</summary>
        public override object? ExecuteScalar() => throw new NotSupportedException();
        /// <summary>禁止丢失令牌的同步reader路径。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>捕获参数和令牌，等待/异常仅注入测试命令边界。</summary>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sqls.Add(CommandText); connection.Transactions.Add(DbTransaction); connection.Tokens.Add(token); connection.Token = token; connection.LastTransaction = DbTransaction;
            connection.Values.Add(parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value));

            if (connection.Before is not null) await connection.Before(connection.Commands,token);
            var reader=connection.Steps[connection.Commands-1].CreateDataReader(); connection.Readers.Add(reader); return reader;
        }
        /// <summary>只释放当前参数容器。</summary>
        protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>不读取配置或连接网络，错误分类沿现有PG实现。</summary>
    private sealed class ReadFactory(ReadConnection connection) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>返回本例连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>夹具审计身份。</summary>
    private sealed class FulfillmentUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_sales_fulfillment__";
        public bool IsAuthenticated => false;
    }
}

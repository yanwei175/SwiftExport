using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实Dapper入口验证管理查询的门禁、映射、冻结分页和类型/令牌；不模拟SQL商业算法。</summary>
[TestClass]
public sealed partial class PostgreSqlPurchaseOrderManagerMigrationTests
{
    /// <summary>两入口均先全局Guard再独立孤儿检查，第三次查询借原事务且不取得业务锁。</summary>
    [TestMethod, DataRow(false,false), DataRow(false,true), DataRow(true,false), DataRow(true,true)]
    public async Task Offline_GatesAndTransaction(bool lines, bool transactional)
    {
        using var f=new ReadFixture(transactional); using var cts=new CancellationTokenSource();
        if(lines) await f.Repository.GetLinesAsync(Guid.Empty,cts.Token); else await f.Repository.SearchAsync(new(),cts.Token);
        Assert.AreEqual(3,f.Connection.Commands); Assert.HasCount(3,f.Connection.Sqls);
        StringAssert.Contains(f.Connection.Sqls[0],"reversal_of_ledger_id"); StringAssert.Contains(f.Connection.Sqls[1],"purchase_line.uid IS NULL");
        Assert.IsFalse(f.Connection.Sqls[1].Contains("purchase_line.is_deleted",StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Sqls[1].Contains("@PurchaseOrderId",StringComparison.Ordinal));
        Assert.AreSame(f.Transaction,f.Session.Transaction); Assert.AreSame(f.Transaction,f.Connection.LastTransaction); Assert.AreEqual(cts.Token,f.Connection.Token);
        Assert.AreEqual(0,f.Connection.EndTransactions); Assert.AreEqual(0,f.Connection.Closes);
        foreach(var sql in f.Connection.Sqls) foreach(var forbidden in new[]{"CREATE ","DROP ","FOR UPDATE","INTO #","OFFSET"}) Assert.IsFalse(sql.Contains(forbidden,StringComparison.Ordinal));
        Assert.IsTrue(f.Connection.Reader is null||f.Connection.Reader.IsClosed);
    }

    /// <summary>全局反向未知优先于孤儿检查，独立门禁原中文与异常类别保留，两入口都不能吞错误。</summary>
    [TestMethod, DataRow(false,0),DataRow(true,0),DataRow(false,1),DataRow(true,1)]
    public async Task Offline_GateFailures(bool lines,int gate)
    {
        using var f=new ReadFixture(true); f.Connection.Scalars=gate==0?new([true]):new([false,true]);
        Func<Task> action=()=>lines?f.Repository.GetLinesAsync(Guid.NewGuid()):f.Repository.SearchAsync(new());
        if(gate==0) await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(action);
        else Assert.AreEqual("存在无法定位采购行的正式采购入库历史，请先完成数据核实与迁移。",(await Assert.ThrowsAsync<InvalidOperationException>(action)).Message);
        Assert.AreEqual(gate+1,f.Connection.Commands); Assert.AreSame(f.Transaction,f.Session.Transaction); Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>一个缓冲工作集承担Count和分页，不C#排序/重算商业状态，全部DTO字段由Dapper还原。</summary>
    [TestMethod,DataRow(1,2,2),DataRow(2,2,2),DataRow(3,2,1),DataRow(8,2,0)]
    public async Task Offline_FrozenPagingAndFullMapping(int page,int size,int count)
    {
        using var f=new ReadFixture(false); var samples=Enumerable.Range(1,5).Select(i=>Sample<PurchaseOrderManagerListItemDto>(i)).ToArray();
        f.Connection.Rows=Rows(samples); var result=await f.Repository.SearchAsync(new(){PageIndex=page,PageSize=size});
        Assert.AreEqual(5,result.TotalCount); Assert.HasCount(count,result.Items); Assert.AreEqual(page,result.PageIndex); Assert.AreEqual(size,result.PageSize);
        foreach(var pair in samples.Skip((page-1)*size).Take(size).Zip(result.Items)) foreach(var p in typeof(PurchaseOrderManagerListItemDto).GetProperties()) Assert.AreEqual(p.GetValue(pair.First),p.GetValue(pair.Second),p.Name);
        Assert.AreEqual(3,f.Connection.Commands); Assert.IsFalse(f.Connection.Values.ContainsKey("Skip"));
        StringAssert.Contains(f.Connection.Sql,"FROM projected p WHERE"); StringAssert.Contains(f.Connection.Sql,"decimal(18,6)");
        foreach(var p in typeof(PurchaseOrderManagerListItemDto).GetProperties())StringAssert.Contains(f.Connection.Sql,"AS \""+p.Name+"\"");
        StringAssert.Contains(f.Connection.Sql,"DESC NULLS LAST"); StringAssert.Contains(f.Connection.Sql,"get_byte(uuid_send(p.purchase_order_id),10)");
        Assert.IsFalse(f.Connection.Sql.Contains("demand_execution_link",StringComparison.Ordinal));
    }

    /// <summary>去重状态显式integer数组及空哨兵，字符串字面转义与等值，MaxValue自然日不溢出。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_Parameters(bool filled)
    {
        using var f=new ReadFixture(false); var date=new DateTime(2026,10,4,17,18,19,DateTimeKind.Utc);
        var request=new PurchaseOrderManagerSearchRequest{Keyword="  a~%_[  ",CustomerCode="  CODE  ",SupplierId=Guid.NewGuid(),OrderDateFrom=date,OrderDateTo=filled?date:DateTime.MaxValue,
            PlannedDeliveryDateFrom=date,PlannedDeliveryDateTo=date,ExpectedCompletionDateFrom=date,ExpectedCompletionDateTo=date,OnlyWithUnlinkedQuantity=true};
        if(filled){request.ConfirmationStatuses=[ConfirmationStatusEnum.已确认,ConfirmationStatusEnum.待确认,ConfirmationStatusEnum.已确认];request.ReadyStatuses=[(PurchaseReadyStatusEnum)9,(PurchaseReadyStatusEnum)9];}
        await f.Repository.SearchAsync(request); var values=f.Connection.Values; var types=f.Connection.Types;
        Assert.AreEqual("%a~~~%~_~[%",values["Keyword"]);Assert.AreEqual("CODE",values["CustomerCode"]);Assert.AreEqual(request.SupplierId,values["SupplierId"]);
        CollectionAssert.AreEqual(filled?new[]{1,0}:new[]{-1},(int[])values["ConfirmationStatuses"]!);Assert.AreEqual(filled,values["LimitConfirmationStatuses"]);
        foreach(var key in new[]{"ConfirmationStatuses","LifecycleStatuses","ReadyStatuses","ReceiptStatuses"})Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,types[key]);
        Assert.AreEqual(date.Date,values["OrderDateFrom"]); Assert.AreEqual(filled?(object)date.Date.AddDays(1):DBNull.Value,values["OrderDateToExclusive"]);
        foreach(var key in new[]{"PlannedDeliveryDateFrom","PlannedDeliveryDateTo","ExpectedCompletionDateFrom","ExpectedCompletionDateTo"}){Assert.AreEqual(DateTime.SpecifyKind(date,DateTimeKind.Unspecified),values[key]);Assert.AreEqual(NpgsqlDbType.Timestamp,types[key]);}
        Assert.AreEqual(((DateTime)values["BusinessToday"]!).AddDays(8),values["DueSoonExclusive"]);
        Assert.AreEqual(NpgsqlDbType.Uuid,types["SupplierId"]);Assert.AreEqual(NpgsqlDbType.Boolean,types["OnlyWithUnlinkedQuantity"]);
        StringAssert.Contains(f.Connection.Sql,"dl.planned_delivery_date<=@PlannedDeliveryDateTo");StringAssert.Contains(f.Connection.Sql,"facts.expected_completion_date<=@ExpectedCompletionDateTo");
    }

    /// <summary>从表全部字段包含Guid.Empty/当前空图片/未知Ready/NULL价格日期，经实际Dapper保持原值。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_LineMapping(bool nulls)
    {
        using var f=new ReadFixture(true);var sample=Sample<PurchaseOrderManagerLineDto>(1);
        if(nulls)foreach(var property in typeof(PurchaseOrderManagerLineDto).GetProperties())if(property.PropertyType==typeof(string)||Nullable.GetUnderlyingType(property.PropertyType)!=null)property.SetValue(sample,null);
        sample.ProductId=Guid.Empty; f.Connection.Rows=Rows(new[]{sample});var uid=Guid.NewGuid();var actual=(await f.Repository.GetLinesAsync(uid)).Single();
        foreach(var p in typeof(PurchaseOrderManagerLineDto).GetProperties())Assert.AreEqual(p.GetValue(sample),p.GetValue(actual),p.Name);
        Assert.AreEqual(uid,f.Connection.Values["PurchaseOrderId"]);StringAssert.Contains(f.Connection.Sql,"product.main_image");StringAssert.Contains(f.Connection.Sql,"ELSE 0 END AS \"UnreceivedQty\"");
        foreach(var p in typeof(PurchaseOrderManagerLineDto).GetProperties())StringAssert.Contains(f.Connection.Sql,"AS \""+p.Name+"\"");
        Assert.IsFalse(f.Connection.Sql.Contains("min_unit",StringComparison.Ordinal));
    }

    /// <summary>ADO异步边界传播取消或技术失败，两入口及三阶段不冒充服务器等待取消证据。</summary>
    [TestMethod,DataRow(false,0),DataRow(true,0),DataRow(false,1),DataRow(true,1),DataRow(false,2),DataRow(true,2)]
    public async Task Offline_ExecutingCancellation(bool lines,int stage)
    {
        using var f=new ReadFixture(true);using var cts=new CancellationTokenSource();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Connection.Before=async token=>{if(f.Connection.Commands!=stage+1)return;entered.SetResult();await Task.Delay(Timeout.Infinite,token);};
        Task read=lines?f.Repository.GetLinesAsync(Guid.NewGuid(),cts.Token):f.Repository.SearchAsync(new(),cts.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));cts.Cancel();await Assert.ThrowsAsync<OperationCanceledException>(()=>read);
        Assert.AreEqual(stage+1,f.Connection.Commands);Assert.AreSame(f.Transaction,f.Session.Transaction);Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>预取消不执行命令；未知故障原实例传播，不转换零收货。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task Offline_PreCancellationAndFailure(bool lines)
    {
        using var f=new ReadFixture(false); var token=new CancellationToken(true);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>lines?f.Repository.GetLinesAsync(Guid.NewGuid(),token):f.Repository.SearchAsync(new(),token));Assert.AreEqual(0,f.Connection.Commands);
        var error=new InvalidOperationException("技术失败");f.Connection.Before=_=>throw error;
        Assert.AreSame(error,await Assert.ThrowsAsync<InvalidOperationException>(()=>lines?f.Repository.GetLinesAsync(Guid.NewGuid()):f.Repository.SearchAsync(new())));
    }

    /// <summary>实际reader关闭后数据源变化不影响已缓冲的计数/页数据；只证明ADO冻结边界，不冒充PG跨连接。</summary>
    [TestMethod]
    public async Task Offline_DataChangesAfterReaderCloses()
    {
        using var f=new ReadFixture(false);f.Connection.Rows=Rows(Enumerable.Range(1,5).Select(Sample<PurchaseOrderManagerListItemDto>));
        f.Connection.AfterBuffered=()=>f.Connection.Rows.Clear();var result=await f.Repository.SearchAsync(new(){PageIndex=2,PageSize=2});
        Assert.AreEqual(0,f.Connection.Rows.Rows.Count);Assert.AreEqual(5,result.TotalCount);Assert.HasCount(2,result.Items);Assert.AreEqual(3,f.Connection.Commands);
    }

    /// <summary>构造丰富DTO样本，数值有意不符合商业规律，确保仓储不在C#重新计算数据库结果。</summary>
    private static T Sample<T>(int number) where T:new()
    {
        var item=new T();foreach(var p in typeof(T).GetProperties())
        {var t=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;object value=t==typeof(Guid)?Guid.NewGuid():t==typeof(string)?" 原值"+number:t==typeof(DateTime)?new DateTime(2026,10,5,12,34,56):t==typeof(decimal)?12.3456m+number:t==typeof(bool)?number%2==0:t.IsEnum?Enum.ToObject(t,number):number;p.SetValue(item,value);}return item;
    }

    /// <summary>构造Dapper可消费列，不实现任何SQL聚合或状态算法。</summary>
    private static DataTable Rows<T>(IEnumerable<T> rows)
    {
        var table=new DataTable();var props=typeof(T).GetProperties();foreach(var p in props){var type=Nullable.GetUnderlyingType(p.PropertyType)??p.PropertyType;table.Columns.Add(p.Name,type.IsEnum?typeof(int):type);}
        foreach(var row in rows)table.Rows.Add(props.Select(p=>{var value=p.GetValue(row);return value is Enum?Convert.ToInt32(value):value??DBNull.Value;}).ToArray());return table;
    }
    /// <summary>独占无网络夹具，不修改或共享旧测试的可变结果集。</summary>
    private sealed class ReadFixture : IDisposable
    {
        public ReadConnection Connection { get; } = new();
        public DbSession Session { get; }
        public DbTransaction? Transaction { get; }
        public PostgreSqlPurchaseOrderManagerQueryRepository Repository { get; }
        /// <summary>构建既有上下文和调用者可空事务。</summary>
        public ReadFixture(bool transactional)
        {
            Session = new(new ReadFactory(Connection)); if (transactional) Session.Transaction = Transaction = Connection.BeginTransaction();
            var user = new WorkspaceUser(); Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        /// <summary>夹具结束才释放会话。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>记录实际命令/参数，脚本结果不解释SQL。</summary>
    private sealed class ReadConnection : DbConnection
    {
        public List<string> Sqls=[]; public Queue<bool> Scalars=new([false,false]); public string Sql = ""; public DataTable Rows = new(); public DbDataReader? Reader;
        public Dictionary<string, object?> Values = []; public Dictionary<string, NpgsqlDbType> Types = [];
        public DbTransaction? LastTransaction; public CancellationToken Token;
        public int Commands; public int Begins; public int EndTransactions; public int Closes;
        public Func<CancellationToken, Task>? Before;
        public Action? AfterBuffered;
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
        /// <summary>记录真实标量调用，脚本只返回门禁命中，不解释SQL。</summary>
        public override async Task<object?> ExecuteScalarAsync(CancellationToken token) { token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql=CommandText; connection.Sqls.Add(CommandText); connection.Token=token; connection.LastTransaction=DbTransaction; if(connection.Before is not null)await connection.Before(token); return connection.Scalars.Dequeue(); }
        /// <summary>禁止丢失令牌的同步reader路径。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>捕获参数和令牌，等待/异常仅注入测试命令边界。</summary>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); connection.Commands++; connection.Sql = CommandText; connection.Sqls.Add(CommandText); connection.Token = token; connection.LastTransaction = DbTransaction;
            connection.Values = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value);
            connection.Types = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType);
            if (connection.Before is not null) await connection.Before(token);
            return connection.Reader = connection.Rows.CreateDataReader();
        }
        /// <summary>只释放当前参数容器。</summary>
        protected override void Dispose(bool disposing)
        {
            if(disposing)
            {
                parameters.Dispose();
                // 只在主投影reader完整关闭后施加测试源变化，不改SQL或返回DTO。
                if(CommandText.Contains("FROM projected p WHERE",StringComparison.Ordinal)&&connection.Reader?.IsClosed==true)connection.AfterBuffered?.Invoke();
            }
            base.Dispose(disposing);
        }
    }

    /// <summary>不读取配置或连接网络，错误分类沿现有PG实现。</summary>
    private sealed class ReadFactory(ReadConnection connection) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>返回本例连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>夹具审计身份。</summary>
    private sealed class WorkspaceUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_gross_workspace_detail__";
        public bool IsAuthenticated => false;
    }
}


using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;
namespace SwiftExport.Application.Tests.Database;

/// <summary>通过公开Manager及Dapper记录四阶段、冻结JSON、参数与DTO；内存数据不作为真实PG事实验证。</summary>
[TestClass]
public sealed class PostgreSqlSalesOrderManagerMigrationTests
{
    /// <summary>候选七字段、一次行事实、SQL汇总count、真实头页依次执行；超末页仍使用独立总数。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Stages_KeepFrozenRelationsAndIndependentCount(bool transaction)
    {
        using var f=new Fixture(transaction);using var c=new CancellationTokenSource();var uid=Guid.NewGuid(); Queue(f,uid,17,false);
        var result=await f.Repository.SearchManagerAsync(new(){PageIndex=99,PageSize=1},new DateTime(2026,10,5,19,1,0),c.Token);
        Assert.AreEqual(17,result.TotalCount);Assert.HasCount(0,result.Items);Assert.AreEqual(99,result.PageIndex);Assert.AreEqual(1,result.PageSize);
        Assert.HasCount(4,f.Connection.Commands);var sql=f.Connection.Commands.Select(x=>x.Sql).ToArray();
        StringAssert.Contains(sql[0],"WHERE h.is_deleted=FALSE");Assert.IsFalse(sql[0].Contains("GrossDemand",StringComparison.Ordinal));
        using var candidate=JsonDocument.Parse((string)f.Connection.Commands[1].Values["Candidates"]!);Assert.AreEqual(7,candidate.RootElement[0].EnumerateObject().Count());Assert.AreEqual(uid,candidate.RootElement[0].GetProperty("Uid").GetGuid());
        StringAssert.Contains(sql[1],"AS \"ShippedQty\"");StringAssert.Contains(sql[2],"filtered AS MATERIALIZED");StringAssert.Contains(sql[2],"COUNT(*)::integer FROM filtered");
        Assert.IsFalse(sql[2].Contains("public.sales_order_header",StringComparison.Ordinal));StringAssert.Contains(sql[3],"JOIN \"public\".\"sales_order_header\" h");
        Assert.IsFalse(sql[3].Contains("h.is_deleted",StringComparison.Ordinal));StringAssert.Contains(sql[3],"NULLS FIRST");StringAssert.Contains(sql[3],"h.created_date DESC NULLS LAST,ARRAY[get_byte(uuid_send(h.uid),10)");
        Assert.AreEqual("[]",f.Connection.Commands[3].Values["OrderFacts"]);Assert.AreEqual(NpgsqlDbType.Jsonb,f.Connection.Commands[3].Types["OrderFacts"]);
        foreach(var command in f.Connection.Commands)Assert.AreSame(f.Session.Transaction,command.Transaction);Assert.AreEqual(c.Token,f.Connection.LastCancellation);
        Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>源页默认1/20及上限200保留，不借用服务以外另一套默认值。</summary>
    [TestMethod]
    [DataRow(0,0,1,20)] [DataRow(-1,-2,1,20)] [DataRow(2,1000,2,200)] [DataRow(3,1,3,1)]
    public async Task Paging_BindsSourceDefaults(int index,int size,int expectedIndex,int expectedSize)
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);var result=await f.Repository.SearchManagerAsync(new(){PageIndex=index,PageSize=size},new DateTime(2026,10,5));
        Assert.AreEqual(expectedIndex,result.PageIndex);Assert.AreEqual(expectedSize,result.PageSize);Assert.AreEqual((expectedIndex-1)*expectedSize,f.Connection.Commands[3].Values["Offset"]);Assert.AreEqual(expectedSize,f.Connection.Commands[3].Values["PageSize"]);
    }

    /// <summary>所有数组和nullable时间真实绑定，集合去空白/去重；日期按日截断并用排他次日上界。</summary>
    [TestMethod]
    public async Task Filters_BindArraysDatesAndCandidateOnlyConditions()
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);var customer=Guid.NewGuid();var date=new DateTime(2026,10,5,23,50,0,DateTimeKind.Utc);
        await f.Repository.SearchManagerAsync(new(){CustomerId=customer,CustomerCodes=[" c ","C"," "],CustomerSkus=[" sku ","SKU"],DocumentStatuses=[SalesOrderDocumentStatusEnum.Open,SalesOrderDocumentStatusEnum.Open],ConfirmationStatuses=[ConfirmationStatusEnum.已确认],OrderDateFrom=date,OrderDateTo=date,EffectiveDeliveryDateTo=date},date);
        var cmd=f.Connection.Commands[0];CollectionAssert.AreEqual(new[]{"c"},(string[])cmd.Values["CustomerCodes"]!);CollectionAssert.AreEqual(new[]{"sku"},(string[])cmd.Values["CustomerSkus"]!);
        CollectionAssert.AreEqual(new[]{0},(int[])cmd.Values["DocumentStatuses"]!);CollectionAssert.AreEqual(new[]{1},(int[])cmd.Values["ConfirmationStatuses"]!);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,cmd.Types["DocumentStatuses"]);Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Text,cmd.Types["CustomerCodes"]);
        Assert.AreEqual(customer,cmd.Values["CustomerId"]);Assert.AreEqual(DBNull.Value,cmd.Values["EffectiveDueFrom"]);
        foreach(var name in new[]{"Today","SoonEnd","OrderDateFrom","OrderDateTo","EffectiveDueFrom","EffectiveDueTo"})Assert.AreEqual(NpgsqlDbType.Timestamp,cmd.Types[name]);
        Assert.AreEqual(DateTime.SpecifyKind(date.Date,DateTimeKind.Unspecified),cmd.Values["Today"]);Assert.AreEqual(DateTime.SpecifyKind(date.Date.AddDays(7),DateTimeKind.Unspecified),cmd.Values["SoonEnd"]);
        Assert.AreEqual(DateTime.SpecifyKind(date.Date.AddDays(1),DateTimeKind.Unspecified),cmd.Values["EffectiveDueTo"]);StringAssert.Contains(cmd.Sql,"COALESCE(due_line.expected_delivery_date,h.delivery_date)<@EffectiveDueTo");
    }

    /// <summary>限定字面模式与源构造器一致，不Trim；SQL结构不含用户文本。</summary>
    [TestMethod]
    [DataRow(" a b ","%%a%b%%")] [DataRow("[ab]","%~[ab]%")] [DataRow("~%_?","%~~~%~__%")]
    [DataRow("x';DROP TABLE sales_order_header;--","%x';DROP%TABLE%sales~_order~_header;--%")]
    public async Task LiteralFilters_KeepExactSourcePattern(string input,string expected)
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);
        await f.Repository.SearchManagerAsync(new(){Keyword=input,OrderNo=input,InvoiceNo=input,CustomerPoNo=input},new DateTime(2026,10,5));
        var cmd=f.Connection.Commands[0];foreach(var name in new[]{"Keyword","OrderNo","InvoiceNo","CustomerPoNo"})Assert.AreEqual(expected,cmd.Values[name]);
        Assert.IsFalse(cmd.Sql.Contains(input,StringComparison.Ordinal));StringAssert.Contains(cmd.Sql,"keyword_line.description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");StringAssert.Contains(cmd.Sql,"c.customer_name COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
    }

    /// <summary>空列表/空白/Guid.Empty不新增条件，缺少有效交期仍绑定typed NULL，未知快捷值不归All。</summary>
    [TestMethod]
    public async Task EmptyFiltersAndUnknownQuick_KeepOriginalBoundary()
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);await f.Repository.SearchManagerAsync(new(){CustomerId=Guid.Empty,Keyword=" ",OrderNo="\t",QuickView=(SalesOrderManagerQuickView)99},new DateTime(2026,10,5));
        var cmd=f.Connection.Commands[0];foreach(var name in new[]{"CustomerId","CustomerCodes","CustomerSkus","Keyword","OrderNo","DocumentStatuses"})Assert.IsFalse(cmd.Values.ContainsKey(name));Assert.AreEqual(99,cmd.Values["QuickView"]);
        Assert.AreEqual(DBNull.Value,cmd.Values["EffectiveDueFrom"]);Assert.AreEqual(DBNull.Value,cmd.Values["EffectiveDueTo"]);Assert.IsFalse(cmd.Sql.Contains("due_line",StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Commands[2].Sql,"@QuickView=8");Assert.IsFalse(f.Connection.Commands[2].Sql.Contains("@QuickView=99",StringComparison.Ordinal));
    }

    /// <summary>SQL而非C#负责单位门槛、NULL求和及原因；超发量与nullable枚举映射保持独立。</summary>
    [TestMethod]
    public async Task QuantityAndDtoMapping_KeepKnownOverage()
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),1,true);var result=await f.Repository.SearchManagerAsync(new(),new DateTime(2026,10,5));var item=result.Items.Single();
        Assert.AreEqual(12m,item.TotalShippedQty);Assert.AreEqual(0m,item.TotalUnshippedQty);Assert.IsNull(item.FulfillmentStatus);Assert.IsTrue(item.NeedsReview);Assert.AreEqual("超发需核实",item.ReviewReason);
        var page=f.Connection.Commands[3].Sql;StringAssert.Contains(page,"f.line_count>0 AND f.unknown_count=0 AND f.unit_count=1 AND f.missing_unit_count=0 THEN f.shipped_qty");
        Assert.IsFalse(page.Contains("f.review_count=0 THEN f.shipped_qty",StringComparison.Ordinal));foreach(var field in typeof(SalesOrderManagerListItemDto).GetProperties())StringAssert.Contains(page,"AS \""+field.Name+"\"");
        var sums=f.Connection.Commands[2].Sql;StringAssert.Contains(sums,"SUM(l.\"ShippedQty\")");StringAssert.Contains(sums,"COUNT(DISTINCT NULLIF(rtrim(l.\"UnitSnapshot\",' '),'') COLLATE public.erp_dictionary_ci)");StringAssert.Contains(page,"SELECT DISTINCT lf.\"ReviewReason\" COLLATE public.erp_dictionary_ci");
    }

    /// <summary>预取消及四阶段失败原样传播，不提交或回滚调用者事务，也不启动后续查询。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public async Task Cancellation_StopsAtSourceStage(int stage)
    {
        using var f=new Fixture(true);using var c=new CancellationTokenSource();Queue(f,Guid.NewGuid(),0,false);if(stage<0)c.Cancel();else f.Connection.BeforeCommand=i=>{if(i==stage)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchManagerAsync(new(),new DateTime(2026,10,5),c.Token));Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>空参数维持源异常并且不发SQL；不将其偷偷改成默认请求。</summary>
    [TestMethod]
    public async Task NullRequest_IsRejectedBeforeSql()
    {
        using var f=new Fixture(false);await Assert.ThrowsAsync<ArgumentNullException>(()=>f.Repository.SearchManagerAsync(null!,DateTime.Today));Assert.HasCount(0,f.Connection.Commands);
    }

    /// <summary>仅构造驱动返回表，不计算筛选或分页；SQL真实性由主控PG测试证明。</summary>
    private static void Queue(Fixture f,Guid uid,int count,bool row)
    {
        f.Connection.Results.Enqueue(Table(("Uid",uid),("Version",1),("DocumentStatus",0),("ConfirmationStatus",0),("CustomerId",Guid.NewGuid()),("OrderNo","ORDER"),("DeliveryDate",new DateTime(2026,10,5))));
        f.Connection.Results.Enqueue(Table(("SalesOrderId",uid),("SalesOrderLineId",Guid.NewGuid()),("LineNumber",1),("Qty",10m),("UnitSnapshot","PCS"),("EffectiveDueDate",new DateTime(2026,10,5)),("ShippedQty",12m),("UnshippedQty",0m),("ReviewReason","超发需核实")));
        f.Connection.Results.Enqueue(Table(("TotalCount",count),("FilteredFactsJson","[]")));
        var items=Table(("Uid",uid),("TotalShippedQty",12m),("TotalUnshippedQty",0m),("FulfillmentStatus",DBNull.Value),("NeedsReview",true),("ReviewReason","超发需核实"));if(!row)items.Rows.Clear();f.Connection.Results.Enqueue(items);
    }

    /// <summary>保持原始nullable字段和显式列名，让实际Dapper映射执行。</summary>
    private static DataTable Table(params (string Name,object Value)[] values)
    {
        var table=new DataTable();foreach(var value in values)table.Columns.Add(value.Name,value.Value is DBNull?typeof(int):value.Value.GetType());table.Rows.Add(values.Select(x=>x.Value).ToArray());return table;
    }
    /// <summary>每例独享内存连接，使用生产会话和审计上下文但不接触网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlSalesOrderQueryRepository Repository;
        public Fixture(bool transaction)
        {
            Session = new(new Factory(Connection));
            if (transaction) Session.Transaction = Connection.BeginTransaction();
            var user = new User();
            Repository = new(new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>只返回指定离线连接。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    { public IDbConnection CreateConnection() => connection; public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance; }

    /// <summary>提供固定测试审计身份，查询不调用打戳。</summary>
    private sealed class User : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "物料来源离线夹具";
        public bool IsAuthenticated => false;
    }

    /// <summary>记录实际查询命令、事务和参数，返回预先构造的数据表；没有提供者执行通道。</summary>
    private sealed class RecordingConnection : DbConnection
    {
        public readonly Queue<DataTable> Results = new();
        public readonly List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction, Dictionary<string, NpgsqlDbType> Types)> Commands = [];
        public CancellationToken LastCancellation;
        public int Begins, Commits, Rollbacks;
        public Action<int>? BeforeCommand;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "test";
        public override ConnectionState State => ConnectionState.Open;
        public override void Open() { }
        public override void Close() { }
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { Begins++; return new Transaction(this); }
        protected override DbCommand CreateDbCommand() => new Command(this);

        /// <summary>仅记录外层事务动作。</summary>
        private sealed class Transaction(RecordingConnection connection) : DbTransaction
        {
            protected override DbConnection DbConnection => connection;
            public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
            public override void Commit() => connection.Commits++;
            public override void Rollback() => connection.Rollbacks++;
        }

        /// <summary>仅借用 Npgsql 参数容器；执行结果完全由内存数据表提供。</summary>
        private sealed class Command(RecordingConnection connection) : DbCommand
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
            public override void Cancel() { }
            public override void Prepare() { }
            public override int ExecuteNonQuery() => throw new NotSupportedException();
            /// <summary>用真实Dapper标量通道读取Guard返回表，不伪造异常分类。</summary>
            public override object ExecuteScalar() { using var reader = ExecuteDbDataReader(CommandBehavior.Default); return reader.Read() ? reader.GetValue(0) : DBNull.Value; }
            /// <summary>标量通道保留原token，命令与数据表由同一记录连接提供。</summary>
            public override Task<object?> ExecuteScalarAsync(CancellationToken token) { connection.LastCancellation=token; token.ThrowIfCancellationRequested(); return Task.FromResult<object?>(ExecuteScalar()); }
            /// <summary>记录执行时SQL、参数类型和外层事务，再返回本阶段数据表。</summary>
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                connection.BeforeCommand?.Invoke(connection.Commands.Count);
                connection.Commands.Add((CommandText, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value), DbTransaction, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType)));
                return connection.Results.Dequeue().CreateDataReader();
            }
            /// <summary>异步查询在执行前观察取消并记录token，不创建数据库连接。</summary>
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}


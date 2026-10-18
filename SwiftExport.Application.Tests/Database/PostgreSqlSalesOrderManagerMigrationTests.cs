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

/// <summary>通过公开管理查询及Dapper记录订单事实、参数与DTO；内存数据不替代真实PG执行。</summary>
[TestClass]
public sealed partial class PostgreSqlSalesOrderManagerMigrationTests
{
    /// <summary>订单事实计数和取页两次读取，超末页保留总数，不查询任何下游表。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Stages_KeepOrderFactsAndIndependentCount(bool transaction)
    {
        using var f=new Fixture(transaction);using var c=new CancellationTokenSource();Queue(f,Guid.NewGuid(),17,false);
        var result=await f.Repository.SearchManagerAsync(new(){PageIndex=99,PageSize=1},c.Token);
        Assert.AreEqual(17,result.TotalCount);Assert.HasCount(0,result.Items);Assert.AreEqual(99,result.PageIndex);Assert.AreEqual(1,result.PageSize);
        Assert.HasCount(2,f.Connection.Commands);
        foreach(var command in f.Connection.Commands)
        {
            StringAssert.Contains(command.Sql,"h.is_deleted=FALSE");
            foreach(var forbidden in new[]{"gross_demand","net_demand","packing_list","demand_execution_link","supplier_progress","LineFacts","QuickView"})
                Assert.IsFalse(command.Sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);
            Assert.AreSame(f.Session.Transaction,command.Transaction);
        }
        StringAssert.Contains(f.Connection.Commands[1].Sql,"h.created_date DESC NULLS LAST,ARRAY[get_byte(uuid_send(h.uid),10)");
        Assert.AreEqual(c.Token,f.Connection.LastCancellation);
        Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);
    }

    /// <summary>保留页号归一化、默认二十条和上限二百。</summary>
    [TestMethod]
    [DataRow(0,0,1,20)] [DataRow(2,999,2,200)]
    public async Task Paging_KeepDefaultsAndCap(int index,int size,int expectedIndex,int expectedSize)
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);
        var result=await f.Repository.SearchManagerAsync(new(){PageIndex=index,PageSize=size});
        Assert.AreEqual(expectedIndex,result.PageIndex);Assert.AreEqual(expectedSize,result.PageSize);
        Assert.AreEqual((expectedIndex-1)*expectedSize,f.Connection.Commands[1].Values["Offset"]);
        Assert.AreEqual(expectedSize,f.Connection.Commands[1].Values["PageSize"]);
    }

    /// <summary>多组事实筛选参数原样绑定，交期EXISTS只选单，汇总没有行日期条件。</summary>
    [TestMethod]
    public async Task Filters_BindArraysDatesAndKeepWholeOrderTotals()
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);var customer=Guid.NewGuid();var date=new DateTime(2026,10,5,23,50,0,DateTimeKind.Utc);
        await f.Repository.SearchManagerAsync(new(){CustomerId=customer,CustomerCodes=[" c ","C"," "],CustomerSkus=[" sku ","SKU"],DocumentStatuses=[SalesOrderDocumentStatusEnum.Open,SalesOrderDocumentStatusEnum.Open],ConfirmationStatuses=[ConfirmationStatusEnum.已确认],OrderDateFrom=date,OrderDateTo=date,EffectiveDeliveryDateTo=date});
        foreach(var cmd in f.Connection.Commands)
        {
            CollectionAssert.AreEqual(new[]{"c"},(string[])cmd.Values["CustomerCodes"]!);CollectionAssert.AreEqual(new[]{"sku"},(string[])cmd.Values["CustomerSkus"]!);
            CollectionAssert.AreEqual(new[]{0},(int[])cmd.Values["DocumentStatuses"]!);CollectionAssert.AreEqual(new[]{1},(int[])cmd.Values["ConfirmationStatuses"]!);
            Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,cmd.Types["DocumentStatuses"]);Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Text,cmd.Types["CustomerCodes"]);
            Assert.AreEqual(customer,cmd.Values["CustomerId"]);Assert.AreEqual(DBNull.Value,cmd.Values["EffectiveDueFrom"]);
            Assert.AreEqual(DateTime.SpecifyKind(date.Date.AddDays(1),DateTimeKind.Unspecified),cmd.Values["EffectiveDueTo"]);
            StringAssert.Contains(cmd.Sql,"COALESCE(due_line.expected_delivery_date,h.delivery_date)<@EffectiveDueTo");
        }
        var sql=f.Connection.Commands[1].Sql;
        StringAssert.Contains(sql,"FROM public.sales_order_line l WHERE l.sales_order_id=h.uid AND l.is_deleted=FALSE");
        Assert.IsFalse(sql.Contains("l.expected_delivery_date",StringComparison.Ordinal));
    }

    /// <summary>保持既有字面转义模式，不拼接用户输入。</summary>
    [TestMethod]
    [DataRow(" a b ","%%a%b%%")] [DataRow("[ab]","%~[ab]%")] [DataRow("~%_?","%~~~%~__%")]
    [DataRow("x';DROP TABLE sales_order_header;--","%x';DROP%TABLE%sales~_order~_header;--%")]
    public async Task LiteralFilters_KeepExactSourcePattern(string input,string expected)
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);
        await f.Repository.SearchManagerAsync(new(){Keyword=input,OrderNo=input,InvoiceNo=input,CustomerPoNo=input});
        foreach(var cmd in f.Connection.Commands)
        {
            foreach(var name in new[]{"Keyword","OrderNo","InvoiceNo","CustomerPoNo"})Assert.AreEqual(expected,cmd.Values[name]);
            Assert.IsFalse(cmd.Sql.Contains(input,StringComparison.Ordinal));
            StringAssert.Contains(cmd.Sql,"keyword_line.description_en COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
        }
    }

    /// <summary>完整映射订单数量、单位及混合/缺失标志，禁止沿用履约门槛。</summary>
    [TestMethod]
    public async Task QuantityAndDtoMapping_OnlyUsesOrderLines()
    {
        using var f=new Fixture(false);var uid=Guid.NewGuid();Queue(f,uid,1,true);
        var item=(await f.Repository.SearchManagerAsync(new())).Items.Single();
        Assert.AreEqual(uid,item.Uid);Assert.AreEqual(10m,item.TotalQty);Assert.AreEqual("PCS",item.UnitSnapshot);
        Assert.IsFalse(item.HasMixedUnits);Assert.IsFalse(item.HasMissingUnit);
        var sql=f.Connection.Commands[1].Sql;
        StringAssert.Contains(sql,"WHEN facts.line_count=0 THEN 0::numeric");
        StringAssert.Contains(sql,"facts.unit_count=1 AND facts.missing_unit_count=0 THEN facts.ordered_qty");
        StringAssert.Contains(sql,"NULLIF(rtrim(l.unit_snapshot,' '),'') COLLATE public.erp_dictionary_ci");
        foreach(var property in typeof(SalesOrderManagerListItemDto).GetProperties())StringAssert.Contains(sql,"AS \""+property.Name+"\"");
    }

    /// <summary>空筛选不新增条件，空交期仍作为typed NULL传递。</summary>
    [TestMethod]
    public async Task EmptyFilters_KeepOriginalBoundary()
    {
        using var f=new Fixture(false);Queue(f,Guid.NewGuid(),0,false);
        await f.Repository.SearchManagerAsync(new(){CustomerId=Guid.Empty,Keyword=" ",OrderNo="\t",CustomerCodes=[],CustomerSkus=[" ","\t"],DocumentStatuses=[],ConfirmationStatuses=[]});
        var cmd=f.Connection.Commands[0];
        foreach(var name in new[]{"CustomerId","CustomerCodes","CustomerSkus","Keyword","OrderNo","DocumentStatuses","ConfirmationStatuses","QuickView"})Assert.IsFalse(cmd.Values.ContainsKey(name));
        Assert.AreEqual(DBNull.Value,cmd.Values["EffectiveDueFrom"]);Assert.AreEqual(DBNull.Value,cmd.Values["EffectiveDueTo"]);
        Assert.AreEqual(NpgsqlDbType.Timestamp,cmd.Types["EffectiveDueFrom"]);Assert.AreEqual(NpgsqlDbType.Timestamp,cmd.Types["EffectiveDueTo"]);
        Assert.IsFalse(cmd.Sql.Contains("due_line",StringComparison.Ordinal));
    }

    /// <summary>预取消和任一查询取消均保留调用方事务，不启动后续读取。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)]
    public async Task Cancellation_StopsAtFactRead(int stage)
    {
        using var f=new Fixture(true);using var c=new CancellationTokenSource();Queue(f,Guid.NewGuid(),0,false);
        if(stage<0)c.Cancel();else f.Connection.BeforeCommand=i=>{if(i==stage)throw new OperationCanceledException(c.Token);};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchManagerAsync(new(),c.Token));
        Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>空请求在发SQL前明确拒绝。</summary>
    [TestMethod]
    public async Task NullRequest_IsRejectedBeforeSql()
    {
        using var f=new Fixture(false);await Assert.ThrowsAsync<ArgumentNullException>(()=>f.Repository.SearchManagerAsync(null!));Assert.HasCount(0,f.Connection.Commands);
    }

    /// <summary>仅准备驱动结果，不在替身中实现SQL筛选或数量算法。</summary>
    private static void Queue(Fixture f,Guid uid,int count,bool row)
    {
        f.Connection.Results.Enqueue(Table(("TotalCount",count)));
        var items=Table(("Uid",uid),("TotalQty",10m),("UnitSnapshot","PCS"),("HasMixedUnits",false),("HasMissingUnit",false));
        if(!row)items.Rows.Clear();f.Connection.Results.Enqueue(items);
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


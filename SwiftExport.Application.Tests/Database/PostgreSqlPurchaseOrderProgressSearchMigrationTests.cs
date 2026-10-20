using System.Text.Json;
using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Progress;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>公开入口记录单次反馈查询，验证数据库分页回执、局部类型及DTO；不模拟PG商业计算。</summary>
[TestClass]
public sealed class PostgreSqlPurchaseOrderProgressSearchMigrationTests
{
    /// <summary>只执行一次反馈SQL；准确count与当前页由数据库同一快照返回，不访问收货。</summary>
    [TestMethod]
    [DataRow(false,1,2,3,2)] [DataRow(true,2,2,3,1)] [DataRow(false,99,2,3,0)] [DataRow(true,1,2,0,0)]
    public async Task DatabasePage_KeepCountAndPage(bool transaction,int page,int size,int count,int rows)
    {
        using var f=new Fixture(transaction);Queue(f,count,page:page,size:size);using var c=new CancellationTokenSource();
        var d=await f.Repository.SearchAsync(new(){PageIndex=page,PageSize=size},c.Token);
        Assert.AreEqual(count,d.TotalCount);Assert.HasCount(rows,d.Items);Assert.AreEqual(page,d.PageIndex);Assert.AreEqual(size,d.PageSize);
        Assert.HasCount(1,f.Connection.Commands);
        var sql=f.Connection.Commands[0].Sql;StringAssert.Contains(sql,"WITH filtered AS (");
        StringAssert.Contains(sql,"LIMIT @PageSize OFFSET @Offset");
        Assert.IsFalse(sql.Contains("inbound_document",StringComparison.Ordinal));
        Assert.AreEqual((long)(page-1)*size,f.Connection.Commands[0].Values["Offset"]);
        Assert.AreEqual(size,f.Connection.Commands[0].Values["PageSize"]);
        StringAssert.Contains(sql,"facts.\"OrderDate\" DESC NULLS LAST");StringAssert.Contains(sql,"get_byte(uuid_send(facts.\"PurchaseOrderId\"),10)");
        if(rows>0)Assert.AreEqual("SQL-"+((page-1)*size),d.Items[0].OrderNo);
        foreach(var command in f.Connection.Commands)Assert.AreSame(f.Session.Transaction,command.Transaction);
        Assert.AreEqual(c.Token,f.Connection.LastCancellation);Assert.AreEqual(transaction?1:0,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>分页保留原输入和原int乘法，服务负责有效范围；仓储不套Manager默认/上限。</summary>
    [TestMethod]
    [DataRow(1,500,3)] [DataRow(3,1,1)]
    public async Task Paging_NoDefaults(int page,int size,int rows)
    {
        using var f=new Fixture(false);Queue(f,3,page:page,size:size);var d=await f.Repository.SearchAsync(new(){PageIndex=page,PageSize=size},default);
        Assert.AreEqual(page,d.PageIndex);Assert.AreEqual(size,d.PageSize);Assert.HasCount(rows,d.Items);
    }

    /// <summary>空集合保留-1哨兵且关闭限制；非空按原枚举Distinct，int数组显式单参数绑定。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Statuses_BindLocalIntArrays(bool limit)
    {
        using var f=new Fixture(false);Queue(f,0);var r=new PurchaseOrderProgressSearchRequest();
        if(limit){r.ConfirmationStatuses=[ConfirmationStatusEnum.已确认,ConfirmationStatusEnum.已确认];r.LifecycleStatuses=[PurchaseOrderLifecycleStatusEnum.Active,PurchaseOrderLifecycleStatusEnum.Closed];}
        await f.Repository.SearchAsync(r,default);var cmd=f.Connection.Commands[0];
        foreach(var name in new[]{"Confirmation","Lifecycle"}){Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,cmd.Types[name+"Statuses"]);Assert.AreEqual(limit,cmd.Values["Limit"+name+"Statuses"]);Assert.AreEqual(NpgsqlDbType.Boolean,cmd.Types["Limit"+name+"Statuses"]);}
        CollectionAssert.AreEqual(limit?new[]{1}:new[]{-1},(int[])cmd.Values["ConfirmationStatuses"]!);
        CollectionAssert.AreEqual(limit?new[]{0,8}:new[]{-1},(int[])cmd.Values["LifecycleStatuses"]!);
    }

    /// <summary>时间保持输入时分秒与墙上Kind，上界加一天在SQL完成，不截Date或改成小于等于。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Dates_BindNullableWithoutTruncation(bool value)
    {
        using var f=new Fixture(false);Queue(f,0);var date=new DateTime(2026,10,5,19,21,32,DateTimeKind.Utc);
        await f.Repository.SearchAsync(new(){PlannedDeliveryDateFrom=value?date:null,PlannedDeliveryDateTo=value?date:null},default);
        var cmd=f.Connection.Commands[0];foreach(var name in new[]{"PlannedDeliveryDateFrom","PlannedDeliveryDateTo"})
        {Assert.AreEqual(NpgsqlDbType.Timestamp,cmd.Types[name]);Assert.AreEqual(value?(object)DateTime.SpecifyKind(date,DateTimeKind.Unspecified):DBNull.Value,cmd.Values[name]);}
        StringAssert.Contains(cmd.Sql,"<@PlannedDeliveryDateTo+INTERVAL '1 day'");
        Assert.IsFalse(cmd.Sql.Contains("date_trunc",StringComparison.Ordinal));
    }

    /// <summary>四文本筛选均保持字面转义，参数化而不拼接用户输入。</summary>
    [TestMethod]
    [DataRow(" a b? ","%a b?%")] [DataRow("~%_[ab]","%~~~%~_~[ab]%")] [DataRow("  ",null)]
    public async Task Text_BindLiteral(string input,string? expected)
    {
        using var f=new Fixture(false);Queue(f,0);await f.Repository.SearchAsync(new(){Keyword=input,OrderNo=input,SupplierKeyword=input,Sku=input},default);
        var cmd=f.Connection.Commands[0];foreach(var name in new[]{"Keyword","OrderNo","SupplierKeyword","Sku"}){Assert.AreEqual(expected is null?DBNull.Value:(object)expected,cmd.Values[name]);Assert.AreEqual(NpgsqlDbType.Text,cmd.Types[name]);}
        StringAssert.Contains(cmd.Sql,"COLLATE public.erp_dictionary_ci LIKE @Keyword ESCAPE '~'");
    }

    /// <summary>选单仅返回订单身份、当前资格与有效行数；不读取供应商反馈或收货。</summary>
    [TestMethod]
    public async Task Projection_ReadsOrderSelectionOnly()
    {
        using var f=new Fixture(false);Queue(f,1);var row=(await f.Repository.SearchAsync(new(),default)).Items.Single();var sql=f.Connection.Commands[0].Sql;
        foreach(var property in typeof(PurchaseOrderProgressListItemDto).GetProperties())StringAssert.Contains(sql,"AS \""+property.Name+"\"");
        Assert.IsNull(row.OrderDate);Assert.IsNull(row.SupplierName);Assert.AreEqual(3,row.LineCount);Assert.IsTrue(row.CanReportProgress);
        StringAssert.Contains(sql,"count(*)::int");
        foreach(var forbidden in new[]{"supplier_progress_report","LATERAL","inventory_ledger","inbound_document","demand_execution_link","ready_qty","expected_ready_date","SUM("})
            Assert.IsFalse(sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);

    }

    /// <summary>旧收货门禁即使返回坏历史也不被调用；仍返回完整反馈事实。</summary>
    [TestMethod]
    public async Task UnrelatedReceiptHistory_DoesNotBlockFeedback()
    {
        using var f=new Fixture(true);Queue(f,1,true);var result=await f.Repository.SearchAsync(new(),default);Assert.HasCount(1,result.Items);
        Assert.HasCount(1,f.Connection.Commands);Assert.HasCount(0,f.Connection.Results);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>反馈命令技术异常原样上交，不转换空页或业务错误。</summary>
    [TestMethod]
    [DataRow(0)]
    public async Task TechnicalFailure_Propagates(int stage)
    {
        using var f=new Fixture(true);Queue(f,1);var failure=new InvalidOperationException("技术失败");
        f.Connection.BeforeCommand=i=>{if(i==stage)throw failure;};
        var ex=await Assert.ThrowsAsync<InvalidOperationException>(()=>f.Repository.SearchAsync(new(),default));Assert.AreSame(failure,ex);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>预取消、命令边界及缓冲后取消均可见，不归零返回。</summary>
    [TestMethod]
    [DataRow(-1)] [DataRow(0)] [DataRow(1)]
    public async Task Cancellation_PreserveToken(int stage)
    {
        using var f=new Fixture(true);Queue(f,2);using var c=new CancellationTokenSource();
        if(stage<0)c.Cancel();else f.Connection.BeforeCommand=i=>{if(i==stage)throw new OperationCanceledException(c.Token);if(stage==1&&i==0)c.Cancel();};
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Repository.SearchAsync(new(),c.Token));
        Assert.AreEqual(stage<0?0:stage,f.Connection.Commands.Count);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>只提供数据库分页回执；本夹具不计算商业筛选或重读收货。</summary>
    private static void Queue(Fixture f,int count,bool invalid=false,int page=1,int size=20)
    {
        var items=Enumerable.Range(0,count).Select(i=>new PurchaseOrderProgressListItemDto{
            PurchaseOrderId=Guid.NewGuid(),OrderNo="SQL-"+i,LineCount=3,CanReportProgress=true}).Skip((page-1)*size).Take(size).ToList();
        var table=new DataTable();table.Columns.Add("TotalCount",typeof(int));table.Columns.Add("ItemsJson",typeof(string));
        table.Rows.Add(count,JsonSerializer.Serialize(items));f.Connection.Results.Enqueue(table);
    }

    /// <summary>每例独享内存会话与可空事务，不接触网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderProgressQueryRepository Repository;
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
        public string UserName => "采购进度列表离线夹具";
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
            /// <summary>用真实Dapper标量通道读取独立count返回表，不伪造异常分类。</summary>
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

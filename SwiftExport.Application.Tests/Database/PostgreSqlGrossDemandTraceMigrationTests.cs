using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning.Sources.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand.Trace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实公开Trace与Dapper验证阶段、映射和资源借用；脚本连接不解释SQL，不作为PG语法验收。</summary>
[TestClass]
public sealed partial class PostgreSqlGrossDemandTraceMigrationTests
{
    /// <summary>完整七阶段在同一连接顺序消费，保留历史物料、nullable反馈及反向展示，零仓储事务副作用。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_StagesProjectionAndBorrowedTransaction(bool transactional)
    {
        using var f = new ScriptFixture(transactional);
        using var cancellation = new CancellationTokenSource();
        var result = await f.Repository.GetTraceAsync(f.Id, cancellation.Token);
        Assert.IsNotNull(result); Assert.AreEqual(f.Id, result.GrossDemandId); Assert.AreEqual("MD", result.SourceType);
        Assert.AreEqual("SRC", result.SourceNo); Assert.AreEqual(10m, result.DemandQty); Assert.IsNull(result.SourceLineNumber);
        Assert.IsNull(result.RequiredDate); Assert.AreEqual("SKU", result.Sku); Assert.AreEqual("", result.CustomerSku);
        Assert.HasCount(2, result.Materials); Assert.IsNull(result.Materials[1].ActivePurchaseQty);
        Assert.AreEqual("历史拆解修订", result.Materials[1].ReviewReason);
        var purchase = result.Purchases.Single(); Assert.AreEqual(6m, purchase.LinkedQty); Assert.AreEqual(12m, purchase.PurchaseLineQty);
        Assert.AreEqual(7m, purchase.PurchaseLineReceivedQty); Assert.IsNull(purchase.PurchaseLineReadyQty); Assert.IsNull(purchase.ExpectedReadyDate);
        Assert.IsTrue(purchase.HasPurchaseTarget); Assert.IsTrue(purchase.IsSharedPurchaseLine);
        Assert.HasCount(2, result.Deliveries); Assert.AreEqual(4m, result.DeliveredQty);
        Assert.AreEqual(3m, result.Deliveries.Single(row => !row.CountsTowardDelivery).Qty);
        Assert.AreEqual("", result.ReviewReason); AssertBorrowed(f, 7, cancellation.Token);
        CollectionAssert.AreEqual(new[] { f.PurchaseLine }, (Guid[])f.Connection.Calls[1].Values["PurchaseLineIds"]!);
        Assert.AreEqual(true, f.Connection.Calls[1].Values["Scoped"]);
        Assert.IsFalse(f.Connection.Calls[0].Sql.Contains("is_deleted", StringComparison.Ordinal));
        Assert.IsFalse(f.Connection.Calls[3].Sql.Contains("WHERE n.gross_demand_id=@Uid AND n.is_deleted", StringComparison.Ordinal));
        var sql = f.Connection.Calls[4].Sql;
        StringAssert.Contains(sql, "pol.quantity"); Assert.IsFalse(sql.Contains("pol.qty", StringComparison.Ordinal));
        StringAssert.Contains(sql, "report.sequence DESC,ARRAY[get_byte(uuid_send(report.uid),10)");
        Assert.IsFalse(sql.Contains("other.target_type", StringComparison.Ordinal));
        StringAssert.Contains(f.Connection.Calls[6].Sql, "rtrim(pl.unit_snapshot,' ')");
    }

    /// <summary>坏来源证据仅影响来源理由，不能抹去已有确定交付；C#大小写/空白判定不借用ICU。</summary>
    [TestMethod]
    [DataRow("fingerprint-null")][DataRow("fingerprint-short")][DataRow("fingerprint-lower")]
    [DataRow("source-id")][DataRow("source-line")][DataRow("source-no")][DataRow("source-kind")]
    [DataRow("md-version")][DataRow("so-version-null")][DataRow("so-version-zero")]
    public async Task Offline_SourceReasonsDoNotInvalidateDelivery(string mode)
    {
        using var f = new ScriptFixture(false); var row = f.Connection.Steps[2].Rows.Rows[0];
        switch (mode)
        {
            case "fingerprint-null": row["DemandBasisFingerprint"] = DBNull.Value; break;
            case "fingerprint-short": row["DemandBasisFingerprint"] = "A"; break;
            case "fingerprint-lower": row["DemandBasisFingerprint"] = new string('a', 64); break;
            case "source-id": row["SourceUid"] = Guid.Empty; break;
            case "source-line": row["SourceLineId"] = Guid.Empty; break;
            case "source-no": row["SourceNo"] = "\t\u3000"; break;
            case "source-kind": row["SourceType"] = "md"; break;
            case "md-version": row["SourceVersion"] = 1; break;
            case "so-version-null": row["SourceType"] = "SO"; break;
            case "so-version-zero": row["SourceType"] = "SO"; row["SourceVersion"] = 0; break;
        }
        var result = await f.Repository.GetTraceAsync(f.Id);
        Assert.AreEqual(4m, result!.DeliveredQty); Assert.AreEqual("来源身份、版本或需求指纹需核实", result.ReviewReason);
    }

    /// <summary>交付客户、单位、展示理由及尾部隐蔽坏历史分别阻断净量，不把NULL改为零。</summary>
    [TestMethod, DataRow("customer"), DataRow("unit"), DataRow("display"), DataRow("hidden")]
    public async Task Offline_DeliveryEvidenceIndependentlyInvalidatesQuantity(string mode)
    {
        using var f = new ScriptFixture(true);
        if (mode == "customer") f.Connection.Steps[2].Rows.Rows[0]["CustomerId"] = DBNull.Value;
        if (mode == "unit") f.Connection.Steps[2].Rows.Rows[0]["Unit"] = "\t\u3000";
        if (mode == "display") f.Connection.Steps[5].Rows.Rows[0]["ReviewReason"] = "交付单位需核实";
        if (mode == "hidden") f.Connection.Steps[6].Scalar = 1;
        var result = await f.Repository.GetTraceAsync(f.Id); Assert.IsNull(result!.DeliveredQty);
        Assert.IsFalse(string.IsNullOrWhiteSpace(result.ReviewReason)); AssertBorrowed(f, 7, default);
    }

    /// <summary>无头先缓冲第二集合才返回，不查询采购或尾部；该集合失败仍可见。</summary>
    [TestMethod, DataRow(false), DataRow(true)]
    public async Task Offline_EmptyHeaderStillConsumesMaterials(bool fail)
    {
        using var f = new ScriptFixture(true); f.Connection.Steps[2].Rows.Rows.Clear();
        var failure = new InvalidOperationException("第二阶段失败"); if (fail) f.Connection.Steps[3].Failure = failure;
        if (fail) Assert.AreSame(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Repository.GetTraceAsync(f.Id)));
        else Assert.IsNull(await f.Repository.GetTraceAsync(f.Id));
        AssertBorrowed(f, 4, default);
    }

    /// <summary>各阶段失败立即停止，无后续查询且不释放或回滚调用者事务；已结束reader全部关闭。</summary>
    [TestMethod, DataRow(0), DataRow(1), DataRow(2), DataRow(3), DataRow(4), DataRow(5), DataRow(6)]
    public async Task Offline_StageFailurePreservesOwnerAndStops(int stage)
    {
        using var f = new ScriptFixture(true); var failure = new InvalidOperationException("阶段失败");
        f.Connection.Steps[stage].Failure = failure;
        Assert.AreSame(failure, await Assert.ThrowsAsync<InvalidOperationException>(() => f.Repository.GetTraceAsync(f.Id)));
        AssertBorrowed(f, stage + 1, default);
    }

    /// <summary>等待中取消真实透传至每个ADO异步执行，保留令牌、事务和原取消异常。</summary>
    [TestMethod, DataRow(0), DataRow(1), DataRow(2), DataRow(3), DataRow(4), DataRow(5), DataRow(6)]
    public async Task Offline_CancellationWhileExecutingEveryStage(int stage)
    {
        using var f = new ScriptFixture(true); using var cancellation = new CancellationTokenSource();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Connection.Steps[stage].Wait = async token => { entered.SetResult(); await Task.Delay(Timeout.Infinite, token); };
        var read = f.Repository.GetTraceAsync(f.Id, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)); cancellation.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => read);
        Assert.AreEqual(cancellation.Token, error.CancellationToken); AssertBorrowed(f, stage + 1, cancellation.Token);
    }

    /// <summary>预取消不返回空成功，Guard命中先于空头；原服务通过真实Trace转发保留业务消息。</summary>
    [TestMethod]
    public async Task Offline_GuardServiceAndCancellationKeepOriginalBoundary()
    {
        using var f = new ScriptFixture(false);
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Repository.GetTraceAsync(f.Id, new CancellationToken(true)));
        Assert.HasCount(0, f.Connection.Calls);
        f.Connection.Steps[1].Scalar = true; f.Connection.Steps[2].Rows.Rows.Clear();
        var service = new GrossDemandQueryService(new ServiceBaseContext(f.Session, NullLoggerFactory.Instance), new TraceAdapter(f.Repository));
        var result = await service.GetTraceAsync(f.Id);
        Assert.IsFalse(result.Success); Assert.AreEqual("正式入库反向历史需核实，收货量未知。", result.Message);
        Assert.HasCount(2, f.Connection.Calls);
    }

    /// <summary>原数量为零及多个空集合保持确定零，既有源版本允许SO/CR正版本。</summary>
    [TestMethod, DataRow("MD"), DataRow("SO"), DataRow("CR")]
    public async Task Offline_EmptyCollectionsKeepZeroAndValidSourceKinds(string kind)
    {
        using var f = new ScriptFixture(false); var row = f.Connection.Steps[2].Rows.Rows[0]; row["SourceType"] = kind;
        if (kind != "MD") row["SourceVersion"] = 2;
        foreach (var stage in new[] { 3, 4, 5 }) f.Connection.Steps[stage].Rows.Rows.Clear();
        var result = await f.Repository.GetTraceAsync(f.Id); Assert.AreEqual(0m, result!.DeliveredQty);
        Assert.AreEqual("", result.ReviewReason); Assert.HasCount(0, result.Materials); Assert.HasCount(0, result.Purchases); Assert.HasCount(0, result.Deliveries);
    }

    /// <summary>断言所有实际执行命令借用原事务与令牌，没有并发reader、DDL或仓储连接管理。</summary>
    private static void AssertBorrowed(ScriptFixture fixture, int count, CancellationToken token)
    {
        Assert.HasCount(count, fixture.Connection.Calls);
        foreach (var call in fixture.Connection.Calls)
        {
            Assert.AreSame(fixture.Transaction, call.Transaction); Assert.AreEqual(token, call.Token);
            foreach (var forbidden in new[] { "CREATE ", "DROP ", "INTO #", "FOR UPDATE", "HOLDLOCK", "BEGIN TRAN", "SELECT *" })
                Assert.IsFalse(call.Sql.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        }
        Assert.AreSame(fixture.Transaction, fixture.Session.Transaction); Assert.AreEqual(0, fixture.Connection.EndTransactions);
        Assert.AreEqual(1, fixture.Connection.Opens);
        Assert.AreEqual(fixture.Transaction is null ? 0 : 1, fixture.Connection.BeginTransactions);
        Assert.AreEqual(0, fixture.Connection.Closes); Assert.IsTrue(fixture.Connection.Readers.All(reader => reader.IsClosed));
    }

    /// <summary>用明确列类型构造Dapper真实映射输入，不反射生产方法或替换SQL运算。</summary>
    private static DataTable Table(params (string Name, Type Type, object? Value)[] cells)
    {
        var table = new DataTable(); foreach (var cell in cells) table.Columns.Add(cell.Name, cell.Type);
        if (cells.Length != 0) table.Rows.Add(cells.Select(cell => cell.Value ?? DBNull.Value).ToArray());
        return table;
    }

    /// <summary>测试专用接口适配器仅转发真实Trace；其他接口明确不支持，不代表生产完整接口实现。</summary>
    private sealed class TraceAdapter(PostgreSqlGrossDemandQueryRepository repository) : IGrossDemandQueryRepository
    {
        /// <summary>转发本轮已实现的方法。</summary>
        public Task<GrossDemandTraceDetailDto?> GetTraceAsync(Guid uid, CancellationToken token) => repository.GetTraceAsync(uid, token);
        /// <summary>本夹具不承接工作台。</summary>
        public Task<PagedResponseBase<GrossDemandListItemDto>> SearchWorkspaceAsync(GrossDemandSearchRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <summary>本夹具不承接来源分组。</summary>
        public Task<IReadOnlyList<GrossDemandSourceGroupDto>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken token) => throw new NotSupportedException();
        /// <summary>本夹具不承接工作台详情。</summary>
        public Task<GrossDemandDetailDto?> GetWorkspaceDetailAsync(Guid uid, CancellationToken token) => throw new NotSupportedException();
    }

    /// <summary>每个测试独立构造无网络会话、预期阶段结果及唯一身份。</summary>
    private sealed class ScriptFixture : IDisposable
    {
        public Guid Id { get; } = Guid.NewGuid();
        public Guid PurchaseLine { get; } = Guid.NewGuid();
        public NpgsqlConnection Connection { get; } = new();
        public DbSession Session { get; }
        public DbTransaction? Transaction { get; }
        public PostgreSqlGrossDemandQueryRepository Repository { get; }
        /// <summary>建立真实Dapper所需列和可空调用者事务。</summary>
        public ScriptFixture(bool transactional)
        {
            Session = new(new ScriptFactory(Connection));
            if (transactional) Session.Transaction = Transaction = Connection.BeginTransaction();
            var user = new TraceUser(); Repository = new(new RepositoryBaseContext(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
            Connection.Steps.Add(new() { Rows = Table(("PurchaseLineId", typeof(Guid), PurchaseLine)) });
            Connection.Steps.Add(new() { Scalar = false });
            Connection.Steps.Add(new() { Rows = Table(("GrossDemandId", typeof(Guid), Id), ("SourceType", typeof(string), "MD"),
                ("SourceUid", typeof(Guid), Id), ("SourceLineId", typeof(Guid), Id), ("SourceNo", typeof(string), "SRC"),
                ("SourceLineNumber", typeof(int), null), ("CustomerName", typeof(string), "客户"), ("CustomerId", typeof(Guid), Guid.NewGuid()),
                ("Sku", typeof(string), "SKU"), ("CustomerSku", typeof(string), ""), ("Unit", typeof(string), "PCS"),
                ("DemandQty", typeof(decimal), 10m), ("RequiredDate", typeof(DateTime), null), ("LifecycleStatus", typeof(int), 0),
                ("SourceVersion", typeof(int), null), ("DemandBasisFingerprint", typeof(string), new string('A', 64))) });
            var materials = Table(("NetDemandId", typeof(Guid), Guid.NewGuid()), ("DecompositionRevision", typeof(int), 2),
                ("LineNumber", typeof(int), 1), ("LifecycleStatus", typeof(int), 0), ("Sku", typeof(string), "M"),
                ("MaterialType", typeof(string), ""), ("NetQty", typeof(decimal), 6m), ("ActivePurchaseQty", typeof(decimal), 6m), ("ReviewReason", typeof(string), ""));
            materials.Rows.Add(Guid.NewGuid(), 1, 1, 8, "OLD", "", 6m, DBNull.Value, "历史拆解修订");
            Connection.Steps.Add(new() { Rows = materials });
            Connection.Steps.Add(new() { Rows = Table(("NetDemandId", typeof(Guid), Guid.NewGuid()), ("PurchaseOrderId", typeof(Guid), Guid.NewGuid()),
                ("PurchaseLineId", typeof(Guid), PurchaseLine), ("HasPurchaseTarget", typeof(bool), true), ("PurchaseOrderNo", typeof(string), "PO"),
                ("PurchaseLineNumber", typeof(int), 1), ("Sku", typeof(string), "M"), ("Unit", typeof(string), "PCS"),
                ("ConfirmationStatus", typeof(int), 1), ("LifecycleStatus", typeof(int), 0), ("AllocatedQty", typeof(decimal), 8m),
                ("ReleasedQty", typeof(decimal), 2m), ("LinkedQty", typeof(decimal), 6m), ("PurchaseLineQty", typeof(decimal), 12m),
                ("PurchaseLineReadyQty", typeof(decimal), null), ("PurchaseLineReceivedQty", typeof(decimal), 7m),
                ("ExpectedReadyDate", typeof(DateTime), null), ("IsSharedPurchaseLine", typeof(bool), true), ("ReviewReason", typeof(string), "")) });
            var deliveries = Table(("PackingListId", typeof(Guid), Guid.NewGuid()), ("PackingListLineId", typeof(Guid), Guid.NewGuid()),
                ("PackingListNo", typeof(string), "PL"), ("LineNumber", typeof(int), 1), ("Qty", typeof(decimal), 4m), ("Unit", typeof(string), "PCS"),
                ("CountsTowardDelivery", typeof(bool), true), ("PostedDate", typeof(DateTime), new DateTime(2026, 9, 2)),
                ("ReversedDate", typeof(DateTime), null), ("ReviewReason", typeof(string), ""));
            deliveries.Rows.Add(Guid.NewGuid(), Guid.NewGuid(), "REVERSED", 1, 3m, "PCS", false, new DateTime(2026, 9, 2), new DateTime(2026, 9, 3), "");
            Connection.Steps.Add(new() { Rows = deliveries }); Connection.Steps.Add(new() { Scalar = 0 });
        }
        /// <summary>仅夹具生命周期结束才释放会话。</summary>
        public void Dispose() => Session.Dispose();
    }

    /// <summary>阶段数据及失败/等待控制只存在测试连接，不注入生产回调。</summary>
    private sealed class ScriptStep
    {
        public DataTable Rows { get; set; } = new();
        public object? Scalar { get; set; }
        public Exception? Failure { get; set; }
        public Func<CancellationToken, Task>? Wait { get; set; }
    }

    /// <summary>记录实际ADO调用，不连接网络；类型名使Dapper采用Npgsql数组绑定能力，不在内存模拟SQL算法。</summary>
    private sealed class NpgsqlConnection : DbConnection
    {
        public List<ScriptStep> Steps { get; } = [];
        public List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction, CancellationToken Token)> Calls { get; } = [];
        public List<DbDataReader> Readers { get; } = [];
        public int BeginTransactions; public int EndTransactions; public int Opens; public int Closes;
        [AllowNull] public override string ConnectionString { get; set; } = "";
        public override string Database => "offline";
        public override string DataSource => "none";
        public override string ServerVersion => "script";
        public override ConnectionState State => ConnectionState.Open;
        /// <summary>记录会话打开。</summary>
        public override void Open() => Opens++;
        /// <summary>记录任何非授权关闭。</summary>
        public override void Close() => Closes++;
        /// <summary>禁止切库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>记录调用者事务，不实际访问数据库。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) { BeginTransactions++; return new ScriptTransaction(this); }
        /// <summary>创建记录命令。</summary>
        protected override DbCommand CreateDbCommand() => new ScriptCommand(this);
    }

    /// <summary>事务身份与副作用计数器。</summary>
    private sealed class ScriptTransaction(NpgsqlConnection connection) : DbTransaction
    {
        public override IsolationLevel IsolationLevel => IsolationLevel.ReadCommitted;
        protected override DbConnection DbConnection => connection;
        /// <summary>记录提交。</summary>
        public override void Commit() => connection.EndTransactions++;
        /// <summary>记录回滚。</summary>
        public override void Rollback() => connection.EndTransactions++;
        /// <summary>记录释放，供断言仓储不夺取所有权。</summary>
        protected override void Dispose(bool disposing) { if (disposing) connection.EndTransactions++; base.Dispose(disposing); }
    }

    /// <summary>使用Npgsql参数容器而执行测试脚本，异步令牌与reader均经过Dapper。</summary>
    private sealed class ScriptCommand(NpgsqlConnection connection) : DbCommand
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
        /// <summary>测试取消由令牌控制，不发网络信号。</summary>
        public override void Cancel() { }
        /// <summary>无需提供者准备。</summary>
        public override void Prepare() { }
        /// <summary>禁止写入。</summary>
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <summary>禁止丢失异步令牌的同步路径。</summary>
        public override object? ExecuteScalar() => throw new NotSupportedException();
        /// <summary>禁止同步reader路径。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
        /// <summary>消费一个阶段并保留标量类型。</summary>
        public override async Task<object?> ExecuteScalarAsync(CancellationToken token) => (await Consume(token)).Scalar;
        /// <summary>消费一个阶段并记录reader，后续命令确认已经释放。</summary>
        protected override async Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
        { var step = await Consume(token); var reader = step.Rows.CreateDataReader(); connection.Readers.Add(reader); return reader; }
        /// <summary>记录真实参数/事务，在执行边界注入等待或技术失败。</summary>
        private async Task<ScriptStep> Consume(CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Assert.IsTrue(connection.Readers.All(reader => reader.IsClosed), "前阶段reader未结束");
            var values = parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value);
            var step = connection.Steps[connection.Calls.Count]; connection.Calls.Add((CommandText, values, DbTransaction, token));
            if (step.Wait is not null) await step.Wait(token);
            if (step.Failure is not null) throw step.Failure;
            return step;
        }
        /// <summary>只释放本命令参数容器。</summary>
        protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
    }

    /// <summary>无网络连接工厂保留PG专用错误分类。</summary>
    private sealed class ScriptFactory(NpgsqlConnection connection) : IDbConnectionFactory
    {
        public IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
        /// <summary>仅返回测试连接。</summary>
        public IDbConnection CreateConnection() => connection;
    }

    /// <summary>夹具身份不具有生产登录权限。</summary>
    private sealed class TraceUser : ICurrentUser
    {
        public Guid? UserId => null;
        public string UserName => "__pgtest_gross_trace__";
        public bool IsAuthenticated => false;
    }
}

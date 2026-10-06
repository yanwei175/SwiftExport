using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Draft;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>记录两个普通读取的真实Dapper命令与映射；离线传输不执行SQL，也不证明PG历史资格或排序计算。</summary>
[TestClass]
public sealed class PostgreSqlPurchaseOrderDraftReadMigrationTests
{
    private static readonly string[] Fields = [
        "Uid","Version","IsDeleted","CreatedDate","CreatedBy","UpdatedDate","UpdatedBy",
        "ConfirmationStatus","LifecycleStatus","OrderNo","BoNo","CustomerPoNo","CustomerId","CustomerCode","CustomerNameSnapshot","Currency","DiscountRate","DiscountAmount",
        "SupplierId","SupplierCode","SupplierName","DemandSourceType","DemandSourceUid","DemandSourceNo",
        "ExecutionRequestId","SoPerson","PoPerson","InletrHoseType","AeratorType","PalletType","BoxType",
        "LabelType","IMType","CartonType","LogoType","ReviewedBy","PoFolder","ZheKouBeiZhu","Remark",
        "OrderDate","ExpectedDate","ReplyDate","ConfirmedDate","ConfirmedBy"];

    /// <summary>空集合及Empty身份只返回空，不开事务、不检查取消、不发查询。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task EmptyInputs_DoNotExecute(bool candidate)
    {
        using var f = new Fixture(false); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        if (candidate) Assert.HasCount(0, await f.Repository.GetSafeAppendCandidatesAsync("SO", Guid.NewGuid(), [Guid.Empty], cancel.Token));
        else Assert.HasCount(0, await f.Repository.GetPurchaseDraftsByIdsAsync([Guid.Empty], cancel.Token));
        Assert.HasCount(0, f.Connection.Commands); Assert.AreEqual(0, f.Connection.Begins);
    }

    /// <summary>来源参数沿原校验、错误消息与参数名，不补新的集合或业务资格校验。</summary>
    [TestMethod]
    [DataRow(null)] [DataRow("")] [DataRow(" \t ")]
    public async Task InvalidSourceType_RejectsBeforeQuery(string? value)
    {
        using var f = new Fixture(false);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => f.Repository.GetSafeAppendCandidatesAsync(value!, Guid.NewGuid(), []));
        Assert.AreEqual("sourceType", error.ParamName); StringAssert.Contains(error.Message, "来源类型不能为空。");
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>空SourceUid保留原异常，合法集合只物化一次并过滤Empty/重复。</summary>
    [TestMethod]
    public async Task SourceUidAndEnumeration_KeepOriginalInputContract()
    {
        using var f = new Fixture(false);
        var error = await Assert.ThrowsAsync<ArgumentException>(() => f.Repository.GetSafeAppendCandidatesAsync("SO", Guid.Empty, []));
        Assert.AreEqual("sourceUid", error.ParamName);
        var id = Guid.NewGuid(); var enumerations = 0;
        IEnumerable<Guid> Input() { enumerations++; yield return Guid.Empty; yield return id; yield return id; }
        QueueCandidate(f); await f.Repository.GetSafeAppendCandidatesAsync(" SO ", Guid.NewGuid(), Input());
        Assert.AreEqual(1, enumerations);
        CollectionAssert.AreEqual(new[] { id }, (Guid[])f.Connection.Commands.Single().Values["SupplierIds"]!);
    }

    /// <summary>原LINQ对NULL集合抛参数异常，空集合优化不吞该异常，也不执行SQL。</summary>
    [TestMethod]
    public async Task NullCollections_KeepEnumerableFailure()
    {
        using var f = new Fixture(false);
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Repository.GetSafeAppendCandidatesAsync("SO", Guid.NewGuid(), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => f.Repository.GetPurchaseDraftsByIdsAsync(null!));
        Assert.HasCount(0, f.Connection.Commands);
    }

    /// <summary>实际参数保留显式uuid数组/text、Trim来源及整数状态，父事务/token均透传且无自有事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Candidate_ParametersTransactionAndDto(bool parent)
    {
        using var f = new Fixture(parent); var supplier = Guid.NewGuid(); var source = Guid.NewGuid();
        QueueCandidate(f); using var cancel = new CancellationTokenSource();
        var dto = (await f.Repository.GetSafeAppendCandidatesAsync("  sO  ", source, [Guid.Empty,supplier,supplier], cancel.Token)).Single();
        var command = f.Connection.Commands.Single();
        Assert.AreEqual("sO", command.Values["SourceType"]); Assert.AreEqual(NpgsqlDbType.Text, command.Types["SourceType"]);
        Assert.AreEqual(source, command.Values["SourceUid"]); Assert.AreEqual(NpgsqlDbType.Uuid, command.Types["SourceUid"]);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, command.Types["SupplierIds"]);
        Assert.AreEqual(0, command.Values["ActiveLifecycle"]); Assert.AreEqual(0, command.Values["PendingConfirmation"]);
        Assert.AreSame(f.Session.Transaction, command.Transaction); Assert.AreEqual(cancel.Token, f.Connection.LastCancellation);
        Assert.AreEqual(parent ? 1 : 0, f.Connection.Begins); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
        Assert.AreEqual("历史采购", dto.OrderNo); Assert.AreEqual(13.25m, dto.UntaxedTotalAmount); Assert.AreEqual(0m, dto.TaxIncludedTotalAmount);
    }

    /// <summary>候选8列、金额快照/全历史关系固定；无执行请求/OriginKind资格及原方法没有的锁。</summary>
    [TestMethod]
    public async Task CandidateSql_KeepQualificationAmountsAndHistory()
    {
        using var f = new Fixture(false); QueueCandidate(f);
        await f.Repository.GetSafeAppendCandidatesAsync("SO", Guid.NewGuid(), [Guid.NewGuid()]);
        var sql = f.Connection.Commands.Single().Sql;
        Assert.AreEqual(8, Regex.Matches(sql, "AS \"[A-Z][A-Za-z]+\"").Count);
        StringAssert.Contains(sql, "LEFT JOIN LATERAL"); StringAssert.Contains(sql, "SUM(COALESCE(l.\"unit_amount\",0))");
        StringAssert.Contains(sql, "SUM(COALESCE(l.\"tax_amount\",0))"); StringAssert.Contains(sql, "l.\"is_deleted\"=FALSE");
        StringAssert.Contains(sql, "h.\"supplier_id\"=ANY(@SupplierIds)");
        StringAssert.Contains(sql, "COALESCE(r.\"is_deleted\",FALSE)=FALSE");
        Assert.AreEqual(2, Regex.Matches(sql, "p.\"purchase_order_id\"=h.\"uid\"").Count);
        StringAssert.Contains(sql, "history.\"uid\"=e.\"source_line_id\"");
        foreach (var absent in new[] { "p.\"is_deleted\"", "history.\"is_deleted\"", "e.\"is_deleted\"", "posting_status", "quantity", "unit_price",
            "execution_request_id", "origin_kind", "customer_id", "FOR UPDATE", "pg_advisory", "SKIP LOCKED" })
            Assert.IsFalse(sql.Contains(absent, StringComparison.OrdinalIgnoreCase), absent);
        StringAssert.Contains(sql, "rtrim(h.\"demand_source_type\",' ') COLLATE public.erp_dictionary_ci");
        StringAssert.Contains(sql, "get_byte(uuid_send(h.\"uid\"),10)");
    }

    /// <summary>批量仅UID/未删，原44列都经Dapper映射；额外三个字段保留CLR默认，不强制来源或状态。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Batch_Keep44ColumnsAndNullableMapping(bool parent)
    {
        using var f = new Fixture(parent); var table = HeadTable(); var uid = (Guid)table.Rows[0]["Uid"]; f.Connection.Results.Enqueue(table);
        using var cancel = new CancellationTokenSource();
        var row = (await f.Repository.GetPurchaseDraftsByIdsAsync([Guid.Empty,uid,uid], cancel.Token)).Single();
        var command = f.Connection.Commands.Single();
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, command.Types["Uids"]);
        CollectionAssert.AreEqual(new[] { uid }, (Guid[])command.Values["Uids"]!);
        Assert.AreSame(f.Session.Transaction, command.Transaction); Assert.AreEqual(cancel.Token, f.Connection.LastCancellation);
        Assert.AreEqual(44, Regex.Matches(command.Sql, " AS \"").Count);
        foreach (var name in Fields)
        {
            StringAssert.Contains(command.Sql, "AS \"" + name + "\"");
            var value = typeof(PurchaseOrderHead).GetProperty(name)!.GetValue(row);
            var expected = table.Rows[0][name];
            if (expected is DBNull) Assert.IsNull(value, name);
            else if (value is Enum) Assert.AreEqual(expected, Convert.ToInt32(value), name);
            else Assert.AreEqual(expected, value, name);
        }
        Assert.AreEqual(0, row.OriginKind); Assert.IsNull(row.ActiveConfirmationId); Assert.IsNull(row.PackagingRequirements);
        var where = command.Sql[command.Sql.IndexOf("WHERE", StringComparison.Ordinal)..command.Sql.IndexOf("ORDER BY", StringComparison.Ordinal)];
        Assert.AreEqual("WHERE h.\"uid\"=ANY(@Uids) AND h.\"is_deleted\"=FALSE\n", where.TrimStart().Replace("\r", ""));
        foreach (var absent in new[] { "\"origin_kind\"", "\"active_confirmation_id\"", "\"packaging_requirements\"", "FOR UPDATE", "pg_advisory" })
            Assert.IsFalse(command.Sql.Contains(absent, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(command.Sql, "CASE WHEN h.\"demand_source_uid\" IS NULL THEN NULL ELSE ARRAY[");
        StringAssert.Contains(command.Sql, "] END ASC NULLS FIRST");
        Assert.AreEqual(parent ? 1 : 0, f.Connection.Begins); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>批量缺失与软删由SQL省略；离线只验证空传输不变成占位或异常。</summary>
    [TestMethod]
    public async Task MissingRows_ReturnEmpty()
    {
        using var f = new Fixture(false); f.Connection.Results.Enqueue(HeadTable(false));
        Assert.HasCount(0, await f.Repository.GetPurchaseDraftsByIdsAsync([Guid.NewGuid()]));
    }

    /// <summary>原驱动/传输异常原样上交，不返回空集合或回滚调用者事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Failure_PropagatesWithoutTransactionOwnership(bool candidate)
    {
        using var f = new Fixture(true); var expected = new InvalidOperationException("离线技术失败");
        f.Connection.BeforeCommand = _ => throw expected;
        var actual = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            if (candidate) await f.Repository.GetSafeAppendCandidatesAsync("SO", Guid.NewGuid(), [Guid.NewGuid()]);
            else await f.Repository.GetPurchaseDraftsByIdsAsync([Guid.NewGuid()]);
        });
        Assert.AreSame(expected, actual); Assert.AreEqual(0, f.Connection.Rollbacks); Assert.IsNotNull(f.Session.Transaction);
    }

    /// <summary>非空预取消通过原CommandDefinition传播，普通读取不把取消转换为空。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Cancellation_Propagates(bool candidate)
    {
        using var f = new Fixture(true); using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            if (candidate) await f.Repository.GetSafeAppendCandidatesAsync("SO", Guid.NewGuid(), [Guid.NewGuid()], cancel.Token);
            else await f.Repository.GetPurchaseDraftsByIdsAsync([Guid.NewGuid()], cancel.Token);
        });
        Assert.HasCount(0, f.Connection.Commands); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>只给传输层固定候选结果，不在内存重新执行资格或商业聚合。</summary>
    private static void QueueCandidate(Fixture f)
    {
        var table = new DataTable();
        foreach (var (name,type) in new[] { ("PurchaseOrderId",typeof(Guid)),("Version",typeof(int)),("OrderNo",typeof(string)),
            ("SourceType",typeof(string)),("SourceUid",typeof(Guid)),("SupplierId",typeof(Guid)),("UntaxedTotalAmount",typeof(decimal)),("TaxIncludedTotalAmount",typeof(decimal)) })
            table.Columns.Add(name,type);
        table.Rows.Add(Guid.NewGuid(),3,"历史采购","sO",Guid.NewGuid(),Guid.NewGuid(),13.25m,0m); f.Connection.Results.Enqueue(table);
    }

    /// <summary>按固定44列构造墙上时间及NULL往返传输表，不模拟过滤或排序。</summary>
    private static DataTable HeadTable(bool row = true)
    {
        var table = new DataTable(); var values = new List<object>();
        foreach (var name in Fields)
        {
            var type = Nullable.GetUnderlyingType(typeof(PurchaseOrderHead).GetProperty(name)!.PropertyType) ?? typeof(PurchaseOrderHead).GetProperty(name)!.PropertyType;
            var dataType = type.IsEnum ? typeof(int) : type; table.Columns.Add(name,dataType);
            values.Add(name is "CustomerId" or "DemandSourceUid" or "DiscountAmount" or "ExpectedDate" ? DBNull.Value :
                dataType == typeof(Guid) ? Guid.NewGuid() : dataType == typeof(int) ? (type.IsEnum ? 0 : 3) :
                dataType == typeof(bool) ? false : dataType == typeof(decimal) ? 0.95m :
                dataType == typeof(DateTime) ? new DateTime(2026,10,5,13,14,15,DateTimeKind.Unspecified) : "历史中文-" + name);
        }
        if (row) table.Rows.Add(values.ToArray()); return table;
    }

    /// <summary>每例只有内存连接与可空父事务；没有数据库访问能力。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlPurchaseOrderDraftRepository Repository;
        /// <summary>只组合离线会话，按测试输入选择是否由夹具建立父事务。</summary>
        public Fixture(bool parent)
        {
            Session = new(new Factory(Connection)); if (parent) Session.Transaction = Connection.BeginTransaction();
            var user = new User(); Repository = new(new(Session,NullLoggerFactory.Instance,new AuditStampService(user),user));
        }
        public void Dispose() => Session.Dispose();
    }

    /// <summary>离线工厂仅返回内存传输连接。</summary>
    private sealed class Factory(RecordingConnection connection) : IDbConnectionFactory
    {
        public IDbConnection CreateConnection() => connection;
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
    }
    /// <summary>固定审计身份；读取不打戳或访问登录信息。</summary>
    private sealed class User : ICurrentUser
    {
        public string UserName => "采购草稿普通读取离线夹具";
        public Guid? UserId => null;
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
        /// <summary>记录夹具显式开始次数，仓储不得调用本入口建立自己的事务。</summary>
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
            /// <summary>标量入口读取预置返回表，不计算商业规则或伪造异常分类。</summary>
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

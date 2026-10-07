using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Sources;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>离线通过真实 Dapper 验证四方法命令与映射；替身不执行 SQL，不作为真实 PG 业务验收。</summary>
[TestClass]
public sealed class PostgreSqlMaterialDemandSourceMigrationTests
{
    /// <summary>四方法允许无事务，有事务时只透传，不自建、提交或回滚；空集仅执行一次 SELECT。</summary>
    [TestMethod]
    [DataRow(0, false)] [DataRow(0, true)]
    [DataRow(1, false)] [DataRow(1, true)]
    [DataRow(2, false)] [DataRow(2, true)]
    [DataRow(3, false)] [DataRow(3, true)]
    public async Task EmptyQueries_BorrowSessionAndSingleCommand(int method, bool transaction)
    {
        using var f = new Fixture(transaction);
        using var cancellation = new CancellationTokenSource();
        f.Connection.Results.Enqueue(new DataTable());
        Assert.AreEqual(0, await InvokeAsync(f, method, cancellation.Token));
        Assert.HasCount(1, f.Connection.Commands);
        Assert.AreSame(f.Session.Transaction, f.Connection.Commands.Single().Transaction);
        Assert.AreEqual(cancellation.Token, f.Connection.LastCancellation);
        Assert.AreEqual(transaction ? 1 : 0, f.Connection.Begins);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
        var sql = f.Connection.Commands.Single().Sql;
        Assert.AreEqual(1, sql.Count(c => c == ';'), "不拼多语句或临时表批次。");
        foreach (var forbidden in new[] { "dbo.", "OUTER APPLY", "FOR JSON PATH", "#", "LIMIT", "OFFSET", "Allocation", "LOCK TABLE" })
            Assert.IsFalse(sql.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
    }

    /// <summary>来源类型与 Uid 均为参数，SQL 注入文本不拼进命令；整来源没有行数截断。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task SourceBoundary_UsesBothParameters(bool purchase)
    {
        using var f = new Fixture(false);
        var id = Guid.NewGuid();
        const string type = "SO';DROP TABLE net_demand;--";
        f.Connection.Results.Enqueue(new DataTable());
        if (purchase) await f.Repository.GetPurchaseCandidatesAsync(new() { SourceType = type, SourceUid = id });
        else await f.Repository.GetSourceDemandCandidatesBySourceAsync(type, id);
        var command = f.Connection.Commands.Single();
        Assert.AreEqual(type, command.Values["SourceType"]);
        Assert.AreEqual(id, command.Values["SourceUid"]);
        Assert.IsFalse(command.Sql.Contains(type, StringComparison.Ordinal));
        StringAssert.Contains(command.Sql, "source_uid = @SourceUid");
        StringAssert.Contains(command.Sql, "rtrim(@SourceType, ' ')");
        Assert.IsFalse(command.Sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>无来源 Uid 时忽略单独类型，保留仓储原行为；入参配对校验仍属于既有服务。</summary>
    [TestMethod]
    public async Task AllPurchaseCandidates_DoNotAddSourceFilter()
    {
        using var f = new Fixture(false);
        f.Connection.Results.Enqueue(new DataTable());
        await f.Repository.GetPurchaseCandidatesAsync(new() { SourceType = "SO" });
        var command = f.Connection.Commands.Single();
        Assert.IsFalse(command.Values.ContainsKey("SourceType"));
        Assert.IsFalse(command.Values.ContainsKey("SourceUid"));
        Assert.IsFalse(command.Sql.Contains("@SourceUid", StringComparison.Ordinal));
        Assert.AreEqual("PO", command.Values["PurchaseTargetType"]);
        Assert.AreEqual("采购", command.Values["PurchaseSupplyType"]);
    }

    /// <summary>空请求优先报 ArgumentNullException，不执行查询；取消同样向调用方传播且不改变事务。</summary>
    [TestMethod]
    public async Task NullRequest_IsCheckedBeforeCancelledQuery()
    {
        using var f = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => f.Repository.GetPurchaseCandidatesAsync(null!, cancellation.Token));
        Assert.HasCount(0, f.Connection.Commands);
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Repository.GetPurchaseCandidatesAsync(new(), cancellation.Token));
        Assert.HasCount(0, f.Connection.Commands);
        Assert.AreEqual(0, f.Connection.Commits);
        Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>待建立 DTO 保留需求技术版本、可空来源版本、快照及空日期，超过 200 行仍完整返回。</summary>
    [TestMethod]
    public async Task SourceCandidates_MapSnapshotsAndAllRows()
    {
        using var f = new Fixture(false);
        var table = Rows(new SourceDemandCandidateDto
        {
            GrossDemandId = Guid.NewGuid(), SourceDemandVersion = 7, SourceVersion = null,
            SourceType = "MD", SourceUid = Guid.NewGuid(), SourceNo = "来源", SourceLineId = Guid.NewGuid(),
            SourceLineNumber = 3, Sku = "快照", LinkSku = "关联快照", ImagePath = "来源图", DemandQty = 2.5m
        });
        for (var i = 1; i < 205; i++) table.ImportRow(table.Rows[0]);
        f.Connection.Results.Enqueue(table);
        var rows = await f.Repository.GetSourceDemandCandidatesBySourceAsync("MD", Guid.NewGuid());
        Assert.HasCount(205, rows);
        Assert.AreEqual(7, rows[0].SourceDemandVersion);
        Assert.IsNull(rows[0].SourceVersion);
        Assert.IsNull(rows[0].RequiredDate);
        Assert.AreEqual("来源图", rows[0].ImagePath);
        StringAssert.Contains(f.Connection.Commands.Single().Sql, "nd.lifecycle_status = 0 AND nd.is_deleted = FALSE");
        StringAssert.Contains(f.Connection.Commands.Single().Sql, "source_line_number NULLS FIRST");
    }

    /// <summary>两个分组 DTO 各自按类型与 Uid 返回，COUNT integer 别名与中文来源编号可映射。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Groups_MapIdentityAndCount(bool purchase)
    {
        using var f = new Fixture(false);
        var id = Guid.NewGuid();
        if (purchase)
        {
            f.Connection.Results.Enqueue(Rows(new PurchaseDraftCandidateSourceGroupDto { SourceType = "SO", SourceUid = id, SourceNo = "编号", CandidateCount = 4 }));
            var row = (await f.Repository.GetPurchaseCandidateSourceGroupsAsync()).Single();
            Assert.AreEqual(id, row.SourceUid); Assert.AreEqual(4, row.CandidateCount);
        }
        else
        {
            f.Connection.Results.Enqueue(Rows(new SourceDemandCandidateGroupDto { SourceType = "SO", SourceUid = id, SourceNo = "编号", CandidateCount = 4 }));
            var row = (await f.Repository.GetSourceDemandCandidateGroupsAsync()).Single();
            Assert.AreEqual(id, row.SourceUid); Assert.AreEqual(4, row.CandidateCount);
        }
        StringAssert.Contains(f.Connection.Commands.Single().Sql, "COUNT(*)::integer AS \"CandidateCount\"");
        StringAssert.Contains(f.Connection.Commands.Single().Sql, "GROUP BY rtrim(");
    }

    /// <summary>真实 Dapper 映射布尔、金额、空单位及私有 JSON；null 字段与空数组保持公共 DTO 的空值边界。</summary>
    [TestMethod]
    [DataRow("[]", 0)]
    [DataRow("[{\"SupplierId\":\"00000000-0000-0000-0000-000000000001\",\"IsActive\":false,\"PurchaseOrderNos\":\"P1,P2\"}]", 1)]
    public async Task PurchaseCandidates_MapPrivateJsonAndNullableValues(string json, int count)
    {
        using var f = new Fixture(false);
        var id = Guid.NewGuid();
        var table = Rows(new PurchaseDraftCandidateDto { Uid = id, Version = 5, NetQty = 10m, AssociatedPurchaseQty = 3.5m, IsCustomerSupplied = true, IsNonStandard = true, ImagePath = "当前图" });
        table.Columns.Add("ParentActualPurchaseSuppliersJson", typeof(string));
        table.Rows[0]["ParentActualPurchaseSuppliersJson"] = json;
        f.Connection.Results.Enqueue(table);
        var row = (await f.Repository.GetPurchaseCandidatesAsync(new())).Single();
        Assert.AreEqual(id, row.Uid); Assert.AreEqual(5, row.Version);
        Assert.AreEqual(6.5m, row.RemainingQty); Assert.IsTrue(row.IsCustomerSupplied); Assert.IsTrue(row.IsNonStandard);
        Assert.IsNull(row.Unit); Assert.IsNull(row.SuggestedUnitPrice); Assert.IsNull(row.DemandRequiredDate);
        Assert.HasCount(count, row.ParentActualPurchaseSuppliers);
        if (count > 0) { Assert.IsNull(row.ParentActualPurchaseSuppliers[0].SupplierCode); Assert.IsFalse(row.ParentActualPurchaseSuppliers[0].IsActive); }
        var sql = f.Connection.Commands.Single().Sql;
        StringAssert.Contains(sql, "NULLIF(btrim(nd.unit_snapshot, ' '), '')");
        StringAssert.Contains(sql, "COALESCE(p.main_image, '') AS \"ImagePath\"");
        StringAssert.Contains(sql, "deliveryTarget.uid = nd.parent_net_demand_id");
        StringAssert.Contains(sql, "deliveryTarget.root_net_demand_id = nd.root_net_demand_id");
        StringAssert.Contains(sql, "deliveryTarget.decomposition_revision = nd.decomposition_revision");
        StringAssert.Contains(sql, "SUM(link.allocated_qty - link.released_qty)");
        Assert.IsFalse(sql.Contains("link.allocated_qty >", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("fingerprint", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>查询引用的物理表均来自现有实体 PG 映射；原 Table 特性不改变 public snake_case 表名。</summary>
    [TestMethod]
    public async Task Tables_UseExistingEntityMetadata()
    {
        using var f = new Fixture(false);
        f.Connection.Results.Enqueue(new DataTable());
        await f.Repository.GetPurchaseCandidatesAsync(new());
        foreach (var entity in new[] { typeof(GrossDemand), typeof(NetDemand), typeof(DemandExecutionLink), typeof(PurchaseOrderHead), typeof(PurchaseOrderLine), typeof(Customer), typeof(Product), typeof(Supplier), typeof(SupplierProduct) })
        {
            var metadata = EntityMetadataCache.GetMetadata(entity);
            StringAssert.Contains(f.Connection.Commands.Single().Sql, $"{PostgreSqlNames.Quote(metadata.SchemaName)}.{PostgreSqlNames.Quote(metadata.TableName)}");
        }
    }

    /// <summary>捕获四条实际生成 SQL，逐个核对实体表别名下的物理列，避免仅证明表存在而漏掉 snake_case 字段错误。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public async Task GeneratedSql_ReferencesOnlyMappedEntityColumns(int method)
    {
        using var f = new Fixture(false);
        f.Connection.Results.Enqueue(new DataTable());
        await InvokeAsync(f, method, default);
        var sql = f.Connection.Commands.Single().Sql;
        var mappings = new[] { typeof(GrossDemand), typeof(NetDemand), typeof(DemandExecutionLink), typeof(PurchaseOrderHead), typeof(PurchaseOrderLine), typeof(Customer), typeof(Product), typeof(Supplier), typeof(SupplierProduct) }
            .Select(EntityMetadataCache.GetMetadata).ToDictionary(m => m.TableName);
        var aliases = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sql, "(?:FROM|JOIN) \\\"public\\\"\\.\\\"(?<table>[a-z_]+)\\\" (?<alias>[a-zA-Z]+)"))
            aliases[match.Groups["alias"].Value] = mappings[match.Groups["table"].Value].Columns.Select(c => c.ColumnName).ToHashSet();
        Assert.IsTrue(aliases.Count >= 2);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(sql, "(?<alias>[a-zA-Z]+)\\.(?<column>[a-z_]+)"))
            if (aliases.TryGetValue(match.Groups["alias"].Value, out var columns))
                Assert.IsTrue(columns.Contains(match.Groups["column"].Value), match.Value + " 未出现在实体PG元数据。");
    }

    /// <summary>所有方法取消均直接向外传播，取消前不读取结果或自行回滚调用方事务。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public async Task CancelledQueries_LeaveTransactionOwnedByCaller(int method)
    {
        using var f = new Fixture(true);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => InvokeAsync(f, method, cancellation.Token));
        Assert.HasCount(0, f.Connection.Commands);
        Assert.AreEqual(1, f.Connection.Begins); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>公共可写标量字段都有显式 CLR 别名；计算剩余量和结构化供应商由现有 DTO/私有 JSON 承担。</summary>
    [TestMethod]
    [DataRow(0)] [DataRow(1)] [DataRow(2)] [DataRow(3)]
    public async Task ProjectionAliases_CoverExistingContracts(int method)
    {
        using var f = new Fixture(false);
        f.Connection.Results.Enqueue(new DataTable());
        await InvokeAsync(f, method, default);
        var dto = method switch { 0 => typeof(SourceDemandCandidateGroupDto), 1 => typeof(SourceDemandCandidateDto), 2 => typeof(PurchaseDraftCandidateSourceGroupDto), _ => typeof(PurchaseDraftCandidateDto) };
        var sql = f.Connection.Commands.Single().Sql;
        foreach (var property in dto.GetProperties().Where(p => p.CanWrite && p.Name != nameof(PurchaseDraftCandidateDto.ParentActualPurchaseSuppliers)))
            StringAssert.Contains(sql, "AS \"" + property.Name + "\"");
        if (method == 3) StringAssert.Contains(sql, "AS \"ParentActualPurchaseSuppliersJson\"");
    }

    /// <summary>当前 schema 不允许写入旧异常 Link；离线明确保留软删除防线和无正剩余附加条件，不能把模拟证据当实库历史验证。</summary>
    [TestMethod]
    public async Task LegacyLinkBoundaries_RemainInGeneratedSql()
    {
        using var f = new Fixture(false);
        f.Connection.Results.Enqueue(new DataTable());
        await f.Repository.GetPurchaseCandidatesAsync(new());
        var sql = f.Connection.Commands.Single().Sql;
        StringAssert.Contains(sql, "COALESCE(link.is_deleted, FALSE) = FALSE");
        StringAssert.Contains(sql, "COALESCE(parentLink.is_deleted, FALSE) = FALSE");
        StringAssert.Contains(sql, "SUM(link.allocated_qty - link.released_qty)");
        Assert.IsFalse(sql.Contains("link.allocated_qty >", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("parentLink.allocated_qty", StringComparison.Ordinal));
    }

    /// <summary>执行指定窄接口方法，统一检查四条路径而不模拟 SQL 筛选结果。</summary>
    private static async Task<int> InvokeAsync(Fixture f, int method, CancellationToken token) => method switch
    {
        0 => (await f.Repository.GetSourceDemandCandidateGroupsAsync(token)).Count,
        1 => (await f.Repository.GetSourceDemandCandidatesBySourceAsync("SO", Guid.NewGuid(), token)).Count,
        2 => (await f.Repository.GetPurchaseCandidateSourceGroupsAsync(token)).Count,
        _ => (await f.Repository.GetPurchaseCandidatesAsync(new(), token)).Count
    };

    /// <summary>只构造可写标量列，让真实 Dapper 处理枚举及 DBNull；集合和计算属性不作为数据库列。</summary>
    private static DataTable Rows<T>(T value)
    {
        var table = new DataTable();
        var properties = typeof(T).GetProperties().Where(p => p.CanWrite && p.PropertyType != typeof(List<PurchaseDraftActualSupplierFactDto>)).ToArray();
        foreach (var property in properties)
        {
            var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            table.Columns.Add(property.Name, type.IsEnum ? Enum.GetUnderlyingType(type) : type);
        }
        table.Rows.Add(properties.Select(p => p.GetValue(value) is { } item
            ? item.GetType().IsEnum ? Convert.ToInt32(item) : item : DBNull.Value).ToArray());
        return table;
    }

    /// <summary>每例独享内存连接，使用生产会话和审计上下文但不接触网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlMaterialDemandSourceRepository Repository;
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
    { public IDbConnection CreateConnection() => connection; }

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
        public readonly List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction)> Commands = [];
        public CancellationToken LastCancellation;
        public int Begins, Commits, Rollbacks;
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
            public override object ExecuteScalar() => throw new NotSupportedException();
            protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            {
                connection.Commands.Add((CommandText, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value), DbTransaction));
                return connection.Results.Dequeue().CreateDataReader();
            }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}

using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>通过真实 Dapper 捕获工作台单命令和局部 JSON；不执行数据库 SQL，不将替身返回值冒称为 PG 事实验证。</summary>
[TestClass]
public sealed class PostgreSqlMaterialDemandWorkspaceMigrationTests
{
    /// <summary>总数与 JSON 页面由一个 MATERIALIZED 工作集返回，空页不丢总数；快捷条件先于 count/page。</summary>
    [TestMethod]
    [DataRow("All", "TRUE")] [DataRow("Unknown", "TRUE")]
    [DataRow("Unassigned", "\"ProcurementStatus\"='未承接'")]
    [DataRow("Partial", "\"ProcurementStatus\"='部分承接'")]
    [DataRow("Assigned", "\"ProcurementStatus\"='已承接'")]
    [DataRow("Overdue", "\"DueHint\"='已逾期'")]
    [DataRow("DueSoon", "\"DueHint\"='临近交期'")]
    [DataRow("Review", "\"ReviewReason\"<>''")]
    public async Task Workspace_UsesSingleMaterializationAndKeepsEmptyPageCount(string quick, string filter)
    {
        using var f = new Fixture(false); f.Page(17, "[]");
        var response = await f.Repository.SearchManagerAsync(new() { PageIndex = 99, PageSize = 2, QuickView = quick });
        Assert.AreEqual(17, response.TotalCount); Assert.HasCount(0, response.Items);
        Assert.AreEqual(99, response.PageIndex); Assert.AreEqual(2, response.PageSize);
        var sql = f.Connection.Commands.Single().Sql;
        StringAssert.Contains(sql, "WITH workspace AS MATERIALIZED (");
        Assert.AreEqual(1, Regex.Matches(sql, "AS MATERIALIZED").Count);
        StringAssert.Contains(sql, "FROM workspace WHERE " + filter);
        StringAssert.Contains(sql, "COUNT(*)::integer FROM filtered");
        StringAssert.Contains(sql, "FROM filtered"); StringAssert.Contains(sql, "jsonb_agg(to_jsonb(page)");
        Assert.AreEqual(1, sql.Count(c => c == ';'));
        foreach (var forbidden in new[] { "SELECT *", "#MaterialWorkspace", "DROP TABLE", "OUTER APPLY", "OBJECT_ID", "ROW_NUMBER", "CURRENT_DATE", "GETDATE" })
            Assert.IsFalse(sql.Contains(forbidden, StringComparison.OrdinalIgnoreCase), forbidden);
        Boundary(f, false);
    }

    /// <summary>直接仓储默认页大小为20，上限200；不沿用服务另一套默认值。</summary>
    [TestMethod]
    [DataRow(-1, 0, 1, 20)] [DataRow(3, 900, 3, 200)] [DataRow(0, 1, 1, 1)]
    public async Task PageDefaults_MatchDirectRepository(int index, int size, int expectedIndex, int expectedSize)
    {
        using var f = new Fixture(false); f.Page(0, "[]");
        var response = await f.Repository.SearchManagerAsync(new() { PageIndex = index, PageSize = size });
        Assert.AreEqual(expectedIndex, response.PageIndex); Assert.AreEqual(expectedSize, response.PageSize);
        var values = f.Connection.Commands.Single().Values;
        Assert.AreEqual((expectedIndex - 1) * expectedSize, values["Offset"]); Assert.AreEqual(expectedSize, values["PageSize"]);
    }

    /// <summary>来源模式完整返回205行，不绑定分页参数；空来源返回PageSize=1并保持已有事务。</summary>
    [TestMethod]
    [DataRow(205)] [DataRow(0)]
    public async Task SourceMode_ReturnsEntireJsonPage(int count)
    {
        using var f = new Fixture(true);
        var source = Guid.NewGuid();
        var rows = Enumerable.Range(0, count).Select(i => new MaterialDemandManagerItemDto { Uid = Guid.NewGuid(), SourceUid = source, LineNumber = i + 1, NetQty = 10m, AssociatedPurchaseQty = null }).ToArray();
        f.Page(count, JsonSerializer.Serialize(rows));
        var response = await f.Repository.SearchManagerAsync(new() { SourceUid = source, PageIndex = 20, PageSize = 1 });
        Assert.AreEqual(count, response.TotalCount); Assert.HasCount(count, response.Items);
        Assert.AreEqual(1, response.PageIndex); Assert.AreEqual(Math.Max(1, count), response.PageSize);
        var command = f.Connection.Commands.Single();
        Assert.AreEqual(source, command.Values["SelectedSourceUid"]);
        Assert.IsFalse(command.Values.ContainsKey("Offset")); Assert.IsFalse(command.Sql.Contains("LIMIT @PageSize", StringComparison.Ordinal));
        Assert.IsTrue(response.Items.All(r => r.RemainingQty is null)); Boundary(f, true);
    }

    /// <summary>三个字面搜索入口复用精确模式，方括号按文本处理，不进入原始 LIKE 字符组解析器。</summary>
    [TestMethod]
    [DataRow("[ab]", "%[ab]%")] [DataRow("~[", "%~~[%")]
    [DataRow("%_~?", "%~%~_~~_%")] [DataRow(" a b ", "%%a%b%%")]
    [DataRow("]\\", "%]\\%")]
    public async Task LiteralKeywords_BindExactEscapedPattern(string input, string expected)
    {
        using var f = new Fixture(false); f.Page(0, "[]");
        await f.Repository.SearchManagerAsync(new() { Keyword = input, SuggestedPurchaseSupplierKeyword = input, ActualPurchaseSupplierKeyword = input });
        var command = f.Connection.Commands.Single();
        foreach (var name in new[] { "Keyword", "SuggestedSupplierKeyword", "ActualSupplierKeyword" }) Assert.AreEqual(expected, command.Values[name]);
        StringAssert.Contains(command.Sql, "ESCAPE '~'");
        Assert.IsFalse(command.Sql.Contains("WITH RECURSIVE", StringComparison.Ordinal));
        Assert.IsFalse(command.Sql.Contains("LikeGroups", StringComparison.Ordinal));
    }

    /// <summary>纯空白忽略筛选；用户文本不进入 SQL，保留未Trim的字面模式。</summary>
    [TestMethod]
    public async Task BlankAndInjectionKeywords_PreserveParameterBoundary()
    {
        using var f = new Fixture(false); f.Page(0, "[]");
        await f.Repository.SearchManagerAsync(new() { Keyword = " \t ", SuggestedPurchaseSupplierKeyword = " " });
        Assert.IsFalse(f.Connection.Commands.Single().Values.ContainsKey("Keyword"));
        Assert.IsFalse(f.Connection.Commands.Single().Values.ContainsKey("SuggestedSupplierKeyword"));
        const string input = "x';DROP TABLE net_demand;--";
        f.Page(0, "[]"); await f.Repository.SearchManagerAsync(new() { Keyword = input });
        Assert.IsFalse(f.Connection.Commands.Last().Sql.Contains(input, StringComparison.Ordinal));
        Assert.AreEqual("%" + input.Replace("_", "~_", StringComparison.Ordinal).Replace(" ", "%", StringComparison.Ordinal) + "%", f.Connection.Commands.Last().Values["Keyword"]);
    }

    /// <summary>数组去重/空集合、实际供应商/单号、日期和布尔筛选按原语义参数化；业务日只有一次墙上时间绑定。</summary>
    [TestMethod]
    public async Task FiltersAndDates_BindTypedArraysAndOriginalBoundaries()
    {
        using var f = new Fixture(false); f.Page(0, "[]");
        var id = Guid.NewGuid(); var from = new DateTime(2026, 10, 1, 12, 30, 0, DateTimeKind.Utc); var to = new DateTime(2026, 10, 5, 19, 0, 0);
        var before = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).Date;
        await f.Repository.SearchManagerAsync(new() { NetDemandIds = [id, id], GrossDemandIds = [id], SourceUids = [id], SourceTypes = ["SO", "SO"], SourceNos = ["单号"], Skus = ["SKU"], CustomerSkus = ["客户型号"], SuggestedPurchaseSupplierId = id, ActualPurchaseSupplierId = id, PurchaseOrderNo = " PO ", DemandRequiredDateFrom = from, DemandRequiredDateTo = to, IsCustomerSupplied = true, IsNonStandard = false });
        var after = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).Date;
        var command = f.Connection.Commands.Single();
        Assert.HasCount(1, (Guid[])command.Values["NetDemandIds"]!); Assert.HasCount(1, (string[])command.Values["SourceTypes"]!);
        Assert.AreEqual("PO", command.Values["PurchaseOrderNo"]); Assert.AreEqual(id, command.Values["ActualPurchaseSupplierId"]);
        Assert.AreEqual(DateTime.SpecifyKind(from, DateTimeKind.Unspecified), command.Values["DemandRequiredDateFrom"]);
        Assert.AreEqual(to.Date.AddDays(1), command.Values["DemandRequiredDateToExclusive"]);
        var business = (DateTime)command.Values["BusinessDate"]!;
        Assert.IsTrue(business == before || business == after); Assert.AreEqual(DateTimeKind.Unspecified, business.Kind);
        StringAssert.Contains(command.Sql, "nd.delivery_mode=1"); StringAssert.Contains(command.Sql, "nd.product_id IS NOT NULL");
        foreach (var name in new[] { "BusinessDate", "DemandRequiredDateFrom", "DemandRequiredDateToExclusive" }) Assert.AreEqual(NpgsqlDbType.Timestamp, command.Types[name]);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Uuid, command.Types["NetDemandIds"]);
        Assert.AreEqual(NpgsqlDbType.Array | NpgsqlDbType.Text, command.Types["SourceTypes"]);
        // 实际采购条件沿源查询只要求头，不为筛选新增目标行资格。
        var actual = command.Sql[(command.Sql.IndexOf("EXISTS (SELECT 1 FROM \"public\".\"demand_execution_link\" filteredLink", StringComparison.Ordinal))..];
        Assert.IsFalse(actual.Contains("filteredLine", StringComparison.Ordinal));
    }

    /// <summary>默认物料与来源均活动；指定历史物料放宽来源活动限制，分组保持仅SourceUid，不改为类型+Uid。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task LifecycleAndGroups_PreserveHistoricalSourceBoundary(bool history)
    {
        using var f = new Fixture(true); f.Page(0, "[]");
        var status = history ? NetDemandLifecycleStatusEnum.RolledBack : (NetDemandLifecycleStatusEnum?)null;
        await f.Repository.SearchManagerAsync(new() { LifecycleStatus = status });
        Assert.AreEqual(!history, f.Connection.Commands.Single().Sql.Contains("gd.lifecycle_status=0", StringComparison.Ordinal));
        var table = new DataTable(); table.Columns.Add("SourceUid", typeof(Guid)); table.Columns.Add("SourceType", typeof(string)); table.Columns.Add("SourceNo", typeof(string)); table.Columns.Add("DemandCount", typeof(int));
        var id = Guid.NewGuid(); table.Rows.Add(id, "SO", "来源", 3); f.Connection.Results.Enqueue(table);
        var groups = await f.Repository.SearchMaterialSourceGroupsAsync(new() { LifecycleStatus = status, SourceTypes = ["SO"] });
        Assert.AreEqual(id, groups.Single().SourceUid); Assert.AreEqual(3, groups.Single().DemandCount);
        var sql = f.Connection.Commands.Last().Sql; StringAssert.Contains(sql.Replace("\r\n", "\n", StringComparison.Ordinal), "GROUP BY gd.source_uid\n");
        Assert.AreEqual(!history, sql.Contains("gd.lifecycle_status=0", StringComparison.Ordinal));
        Assert.AreEqual(history ? 8 : 0, f.Connection.Commands.Last().Values["LifecycleStatus"]);
    }

    /// <summary>局部 JSON 支持整套原 DTO，可空关联量不转0；原工作台未读取的三个建议商品字段明确保持null。</summary>
    [TestMethod]
    public async Task JsonProjection_MapsUnknownQuantityAndAllScalarAliases()
    {
        using var f = new Fixture(false);
        var dto = new MaterialDemandManagerItemDto { Uid = Guid.NewGuid(), Version = 5, LifecycleStatus = NetDemandLifecycleStatusEnum.RolledBack, NetQty = 10m, AssociatedPurchaseQty = null, Unit = null, IsCustomerSupplied = true, ProcurementStatus = "历史", ReviewReason = "", DemandRequiredDate = new DateTime(2026, 10, 5) };
        f.Page(1, JsonSerializer.Serialize(new[] { dto }));
        var result = await f.Repository.SearchManagerAsync(new()); var item = result.Items.Single();
        Assert.AreEqual(dto.Uid, item.Uid); Assert.AreEqual(5, item.Version); Assert.AreEqual(dto.LifecycleStatus, item.LifecycleStatus);
        Assert.IsNull(item.RemainingQty); Assert.IsNull(item.Unit); Assert.AreEqual(dto.DemandRequiredDate, item.DemandRequiredDate);
        var sql = f.Connection.Commands.Single().Sql;
        foreach (var property in typeof(MaterialDemandManagerItemDto).GetProperties().Where(p => p.CanWrite)) StringAssert.Contains(sql, "AS \"" + property.Name + "\"");
        StringAssert.Contains(sql, "NULL::text AS \"SuggestedSupplierSku\"");
        StringAssert.Contains(sql, "btrim(nd.unit_snapshot, ' ')");
        StringAssert.Contains(sql, "COALESCE(p.link_sku,gd.link_sku_snapshot)");
        StringAssert.Contains(sql, "采购承接超量需核实");
    }

    /// <summary>核对实际生成SQL的物理列和布尔方言，以及最终接线所需的完整查询接口与Trace入口。</summary>
    [TestMethod]
    public async Task MetadataAndPartialBoundary_AreExplicit()
    {
        using var f = new Fixture(false); f.Page(0, "[]"); await f.Repository.SearchManagerAsync(new());
        var sql = f.Connection.Commands.Single().Sql;
        var maps = new[] { typeof(GrossDemand), typeof(NetDemand), typeof(DemandExecutionLink), typeof(PurchaseOrderHead), typeof(PurchaseOrderLine), typeof(Customer), typeof(Product), typeof(Supplier) }
            .Select(PostgreSqlEntityMetadata.GetMetadata).ToDictionary(m => m.TableName);
        var aliases = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in Regex.Matches(sql, "(?:FROM|JOIN) \\\"public\\\"\\.\\\"(?<table>[a-z_]+)\\\" (?<alias>[a-zA-Z]+)")) aliases[match.Groups["alias"].Value] = maps[match.Groups["table"].Value].Columns.Select(c => c.ColumnName).ToHashSet();
        foreach (Match match in Regex.Matches(sql, "(?<alias>[a-zA-Z]+)\\.(?<column>[a-z_]+)")) if (aliases.TryGetValue(match.Groups["alias"].Value, out var columns)) Assert.IsTrue(columns.Contains(match.Groups["column"].Value), match.Value);
        Assert.AreEqual(8, maps.Count); Assert.IsTrue(aliases.Count > 8);
        Assert.IsFalse(Regex.IsMatch(sql, "COALESCE\\([^)]*is_deleted[^)]*\\)\\s*=\\s*0"));
        Assert.IsTrue(typeof(PostgreSqlMaterialDemandQueryRepository).IsSealed);
        // PG仓储已经完成公共查询接口，不能继续以早期迁移切片的未接线边界作为验收条件。
        Assert.IsTrue(typeof(IMaterialDemandQueryRepository).IsAssignableFrom(typeof(PostgreSqlMaterialDemandQueryRepository)));
        Assert.IsNotNull(typeof(PostgreSqlMaterialDemandQueryRepository).GetMethod("GetTraceAsync"));
    }

    /// <summary>空请求优先于取消；两个方法取消时不消费结果或修改调用方事务。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task NullAndCancellation_LeaveTransactionWithCaller(bool groups)
    {
        using var f = new Fixture(true); using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        if (groups)
        {
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => f.Repository.SearchMaterialSourceGroupsAsync(null!, cancellation.Token));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Repository.SearchMaterialSourceGroupsAsync(new(), cancellation.Token));
        }
        else
        {
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => f.Repository.SearchManagerAsync(null!, cancellation.Token));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => f.Repository.SearchManagerAsync(new(), cancellation.Token));
        }
        Assert.HasCount(0, f.Connection.Commands); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>两个方法均允许无事务，并原样传递非默认取消令牌；不为了读取创建或完成事务。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(false, true)] [DataRow(true, false)] [DataRow(true, true)]
    public async Task ReadBoundary_ForwardsTransactionAndCancellation(bool groups, bool transaction)
    {
        using var f = new Fixture(transaction); using var cancellation = new CancellationTokenSource();
        if (groups) { f.Connection.Results.Enqueue(new DataTable()); await f.Repository.SearchMaterialSourceGroupsAsync(new(), cancellation.Token); }
        else { f.Page(0, "[]"); await f.Repository.SearchManagerAsync(new(), cancellation.Token); }
        Assert.AreEqual(cancellation.Token, f.Connection.LastCancellation); Boundary(f, transaction);
    }

    /// <summary>确认查询没有创建或完成仓储事务。</summary>
    private static void Boundary(Fixture f, bool transaction)
    {
        Assert.HasCount(1, f.Connection.Commands); Assert.AreSame(f.Session.Transaction, f.Connection.Commands.Single().Transaction);
        Assert.AreEqual(transaction ? 1 : 0, f.Connection.Begins); Assert.AreEqual(0, f.Connection.Commits); Assert.AreEqual(0, f.Connection.Rollbacks);
    }

    /// <summary>每例独享内存连接，使用生产会话和审计上下文但不接触网络。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly RecordingConnection Connection = new();
        public readonly DbSession Session;
        public readonly PostgreSqlMaterialDemandQueryRepository Repository;
        public Fixture(bool transaction)
        {
            Session = new(new Factory(Connection));
            if (transaction) Session.Transaction = Connection.BeginTransaction();
            var user = new User();
            Repository = new(new(Session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        }
        /// <summary>只返回SQL计数和私有JSON页面，不模拟筛选或在夹具中分页。</summary>
        public void Page(int count, string json)
        {
            var table = new DataTable(); table.Columns.Add("TotalCount", typeof(int)); table.Columns.Add("ItemsJson", typeof(string));
            table.Rows.Add(count, json); Connection.Results.Enqueue(table);
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
        public readonly List<(string Sql, Dictionary<string, object?> Values, DbTransaction? Transaction, Dictionary<string, NpgsqlDbType> Types)> Commands = [];
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
                connection.Commands.Add((CommandText, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => (object?)p.Value), DbTransaction, parameters.Parameters.Cast<NpgsqlParameter>().ToDictionary(p => p.ParameterName, p => p.NpgsqlDbType)));
                return connection.Results.Dequeue().CreateDataReader();
            }
            protected override Task<DbDataReader> ExecuteDbDataReaderAsync(CommandBehavior behavior, CancellationToken token)
            { connection.LastCancellation = token; token.ThrowIfCancellationRequested(); return Task.FromResult(ExecuteDbDataReader(behavior)); }
            protected override void Dispose(bool disposing) { if (disposing) parameters.Dispose(); base.Dispose(disposing); }
        }
    }
}

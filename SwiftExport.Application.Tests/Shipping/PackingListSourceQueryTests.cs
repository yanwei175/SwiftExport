using System.Data;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>不连接数据库，验证 Picker 专用查询的参数隔离、准入和分页边界。</summary>
[TestClass]
public sealed class PackingListSourceQueryTests
{
    /// <summary>精确销售发票号或系统售后号在计数前过滤，用户文本参数化，PO 不参与查找。</summary>
    [TestMethod]
    public async Task InvoiceFiltersAreExactAndAppliedBeforeCount()
    {
        var product = Guid.NewGuid();
        var (sql, parameters) = await CaptureAsync(new PackingListSourceSearchRequest
        { ProductId = product, IsNonStandard = false, InvoiceNumbers = [" inv'01 ", "AFS01"] });
        CollectionAssert.AreEqual(new[] { "INV'01", "AFS01" }, (string[])parameters["InvoiceNumbers"]!);
        Assert.AreEqual(product, parameters["ProductId"]); Assert.AreEqual(false, parameters["IsNonStandard"]);
        StringAssert.Contains(sql, "THEN soh.invoice_no WHEN rtrim(s.\"SourceType\",' ') COLLATE public.erp_dictionary_ci='CR' THEN ash.after_sales_no");
        StringAssert.Contains(sql, "=ANY(SELECT rtrim(invoice,' ') FROM unnest(@InvoiceNumbers)");
        Assert.IsFalse(sql.Contains("CustomerPoNo", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("INV'01", StringComparison.Ordinal));
        Assert.IsTrue(sql.IndexOf("unnest(@InvoiceNumbers)", StringComparison.Ordinal) < sql.IndexOf("SELECT count(*)", StringComparison.Ordinal));
    }
    /// <summary>批量词按 OR 参数化，草稿来源无需采购执行关系。</summary>
    [TestMethod]
    public async Task BatchKeywordsAndTargetAreFilteredBeforePaging()
    {
        var (sql, parameters) = await CaptureAsync(new PackingListSourceSearchRequest
        { Keywords = [" A% ", "a%", "B'"], Keyword = "A%" });
        Assert.AreEqual("%A~%%", parameters["Keyword0"]);
        Assert.AreEqual("%B'%", parameters["Keyword1"]);
        // 原测试只调用构造函数，PG 记录实际查询的八个固定参数和两项去重关键词。
        Assert.AreEqual(10, parameters.Count);
        Assert.IsFalse(sql.Contains("B'", StringComparison.Ordinal));
        StringAssert.Contains(sql, "OR (s.\"SourceNo\" COLLATE public.erp_dictionary_ci LIKE @Keyword1");
        Assert.IsFalse(sql.Contains("public.net_demand", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("\"net_demand\"", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("demand_execution_link", StringComparison.Ordinal));
        StringAssert.Contains(sql, "(@ProductId IS NULL OR s.\"ProductId\"=@ProductId)");
        StringAssert.Contains(sql, "(@IsNonStandard IS NULL OR s.\"IsNonStandard\"=@IsNonStandard)");
        StringAssert.Contains(sql, "g.\"is_non_standard\"=TRUE AND g.\"product_id\" IS NULL");
        StringAssert.Contains(sql, "g.\"demand_qty\"-COALESCE(d.\"DeliveredQty\",0)");
        StringAssert.Contains(sql, "s.\"RemainingQty\">0");
    }

    /// <summary>普通与售后采用相同草稿准入，共同条件在分页前生效。</summary>
    [TestMethod]
    public async Task AfterSalesExceptionPreservesCommonEligibilityAndDeliveryProjection()
    {
        var (sql, _) = await CaptureAsync(new PackingListSourceSearchRequest { Keyword = "model" });
        var normalized = System.Text.RegularExpressions.Regex.Replace(sql, @"\s+", " ");
        StringAssert.Contains(normalized, "WHERE s.\"IsActive\"=TRUE AND s.\"CustomerId\"=@CustomerId AND s.\"RemainingQty\">0");
        Assert.IsFalse(normalized.Contains("OR EXISTS", StringComparison.Ordinal));
        StringAssert.Contains(sql, "l.\"is_deleted\"=FALSE AND h.\"is_deleted\"=FALSE AND h.\"posting_status\"=@Posted");
        StringAssert.Contains(sql, "GROUP BY l.\"gross_demand_id\"");
        StringAssert.Contains(sql, "g.\"demand_qty\"-COALESCE(d.\"DeliveredQty\",0)");
        StringAssert.Contains(sql, "g.\"image_path_snapshot\" AS \"ImagePath\"");
        StringAssert.Contains(sql, "SELECT s.* FROM source_delivery s");
        Assert.IsTrue(sql.IndexOf("SELECT count(*)::integer AS \"TotalCount\" FROM candidates", StringComparison.Ordinal) < sql.IndexOf("OFFSET @Skip", StringComparison.Ordinal));
    }

    /// <summary>调用实际 PG 查询，在命令执行边界记录 SQL 与参数后用专用异常结束；禁止连接网络或构造假查询结果。</summary>
    private static async Task<(string Sql, Dictionary<string, object?> Parameters)> CaptureAsync(PackingListSourceSearchRequest request)
    {
        using var connection = new CaptureConnection();
        using var session = new DbSession(new Factory(connection));
        var user = new TestUser();
        var repository = new PostgreSqlPackingListRepository(new RepositoryBaseContext(
            session, NullLoggerFactory.Instance, new AuditStampService(user), user));
        await Assert.ThrowsExactlyAsync<SqlCapturedException>(() => repository.SearchSourcesAsync(request));
        return (connection.Sql, connection.Parameters);
    }

    /// <summary>仅标识命令已经记录，不能掩盖其他仓储或参数绑定异常。</summary>
    private sealed class SqlCapturedException : Exception { }

    /// <summary>只返回内存记录连接，保留 PG 分类器，不读取任何连接字符串。</summary>
    private sealed class Factory(CaptureConnection connection) : IDbConnectionFactory
    {
        /// <summary>提供本次记录连接，不建立真实连接。</summary>
        public IDbConnection CreateConnection() => connection;
        /// <summary>沿用 PG 异常分类，记录异常不用于业务失败判定。</summary>
        public SwiftExport.Core.Interfaces.Data.IDatabaseExceptionClassifier Errors => PostgreSqlExceptionClassifier.Instance;
    }

    /// <summary>只满足上下文身份依赖，来源读取不执行审计打戳。</summary>
    private sealed class TestUser : ICurrentUser
    {
        /// <summary>提供离线身份。</summary>
        public Guid? UserId => Guid.Empty;
        /// <summary>标记本次无网络测试。</summary>
        public string UserName => "箱单来源离线";
        /// <summary>满足仓储上下文，不代表真实用户认证。</summary>
        public bool IsAuthenticated => true;
    }

    /// <summary>只能记录命令；模拟已打开状态以验证实际 Dapper 参数绑定，不提供数据库执行能力。</summary>
    private sealed class CaptureConnection : DbConnection
    {
        internal string Sql = string.Empty;
        internal Dictionary<string, object?> Parameters = new();
        /// <summary>仅为 ADO.NET 属性，不解释或使用连接字符串。</summary>
        [AllowNull] public override string ConnectionString { get; set; } = string.Empty;
        /// <summary>标明无数据库。</summary>
        public override string Database => "offline";
        /// <summary>标明无网络来源。</summary>
        public override string DataSource => "none";
        /// <summary>标明只记录命令。</summary>
        public override string ServerVersion => "capture";
        /// <summary>阻止 Dapper 尝试真实打开连接。</summary>
        public override ConnectionState State => ConnectionState.Open;
        /// <summary>满足 DbSession 的构造生命周期；没有底层网络连接，打开只为空操作。</summary>
        public override void Open() { }
        /// <summary>没有真实连接需要关闭。</summary>
        public override void Close() { }
        /// <summary>禁止切换数据库。</summary>
        public override void ChangeDatabase(string databaseName) => throw new NotSupportedException();
        /// <summary>本查询不应创建事务，意外调用直接失败。</summary>
        protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel) => throw new NotSupportedException();
        /// <summary>返回只记录命令，所有执行在提交到数据库之前结束。</summary>
        protected override DbCommand CreateDbCommand() => new CaptureCommand(this);
    }

    /// <summary>借真实 Npgsql 参数容器记录已组装查询，不委托驱动执行或创建结果集。</summary>
    private sealed class CaptureCommand(CaptureConnection owner) : DbCommand
    {
        private readonly NpgsqlCommand container = new();
        /// <summary>保存实际 SQL。</summary>
        [AllowNull] public override string CommandText { get; set; } = string.Empty;
        /// <summary>保留调用方命令超时。</summary>
        public override int CommandTimeout { get; set; }
        /// <summary>保留 Dapper 指定的命令类型。</summary>
        public override CommandType CommandType { get; set; }
        /// <summary>满足 ADO.NET 设计属性。</summary>
        public override bool DesignTimeVisible { get; set; }
        /// <summary>满足 ADO.NET 更新属性。</summary>
        public override UpdateRowSource UpdatedRowSource { get; set; }
        /// <summary>只关联本次内存连接。</summary>
        protected override DbConnection? DbConnection { get; set; } = owner;
        /// <summary>观察传入事务，不建立真实事务。</summary>
        protected override DbTransaction? DbTransaction { get; set; }
        /// <summary>使用提供者参数容器验证 PG 数组和标量绑定。</summary>
        protected override DbParameterCollection DbParameterCollection => container.Parameters;
        /// <summary>创建实际 PG 参数，不执行驱动命令。</summary>
        protected override DbParameter CreateDbParameter() => new NpgsqlParameter();
        /// <summary>没有服务端执行可取消。</summary>
        public override void Cancel() { }
        /// <summary>不进行服务端预编译。</summary>
        public override void Prepare() => throw new NotSupportedException();
        /// <summary>禁止写入命令。</summary>
        public override int ExecuteNonQuery() => throw new NotSupportedException();
        /// <summary>仅允许原查询的 reader 调用，意外标量执行失败。</summary>
        public override object ExecuteScalar() => throw new NotSupportedException();
        /// <summary>记录实际查询和全部参数后显式终止，其他异常不能被捕获完成异常掩盖。</summary>
        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        {
            owner.Sql = CommandText;
            owner.Parameters = container.Parameters.Cast<NpgsqlParameter>().ToDictionary(parameter => parameter.ParameterName, parameter => (object?)parameter.Value);
            throw new SqlCapturedException();
        }
        /// <summary>释放本人参数容器，不涉及数据库资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing) container.Dispose();
            base.Dispose(disposing);
        }
    }
}

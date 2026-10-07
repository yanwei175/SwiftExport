using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Text.RegularExpressions;
using System.Reflection;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.MasterData;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Application.Tests.Database;

/// <summary>PostgreSQL基础能力离线回归；不打开连接、不创建或清理数据库。</summary>
[TestClass]
public sealed class PostgreSqlFoundationTests
{
    /// <summary>目标工厂只构造未打开的 Npgsql 连接，并提供现有 PG 异常分类器。</summary>
    [TestMethod]
    public void Factories_CreateClosedConnections()
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=unit_test;Username=test");
        using var connection = factory.CreateConnection();
        Assert.IsInstanceOfType<NpgsqlConnection>(connection);
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreSame(PostgreSqlExceptionClassifier.Instance, factory.Errors);
    }

    /// <summary>未确定业务含义的SQLSTATE不能冒充乐观并发或可重试超时。</summary>
    [TestMethod]
    [DataRow("23505", DatabaseFailureKind.UniqueConflict)]
    [DataRow("40P01", DatabaseFailureKind.Deadlock)]
    [DataRow("42P01", DatabaseFailureKind.SchemaMismatch)]
    [DataRow("42703", DatabaseFailureKind.SchemaMismatch)]
    [DataRow("08006", DatabaseFailureKind.ConnectionFailure)]
    [DataRow("40001", DatabaseFailureKind.SerializationFailure)]
    [DataRow("57014", DatabaseFailureKind.ServerQueryCanceled)]
    [DataRow("55P03", DatabaseFailureKind.Unknown)]
    [DataRow("23514", DatabaseFailureKind.Unknown)]
    [DataRow("P3102", DatabaseFailureKind.Unknown)]
    public void ProviderErrors_StayDistinct(string state, DatabaseFailureKind expected)
        => Assert.AreEqual(expected, PostgreSqlExceptionClassifier.Instance.Classify(new PostgresException("测试错误", "ERROR", "ERROR", state)));

    /// <summary>只根据驱动保留的明确原因识别取消、网络失败和超时。</summary>
    [TestMethod]
    public void TransportErrors_DoNotGuessFromMessages()
    {
        var classifier = PostgreSqlExceptionClassifier.Instance;
        Assert.AreEqual(DatabaseFailureKind.Timeout, classifier.Classify(new NpgsqlException("timeout", new TimeoutException())));
        Assert.AreEqual(DatabaseFailureKind.ConnectionFailure, classifier.Classify(new NpgsqlException("network", new IOException())));
        Assert.AreEqual(DatabaseFailureKind.Canceled, classifier.Classify(new OperationCanceledException()));
        Assert.AreEqual(DatabaseFailureKind.Unknown, classifier.Classify(new Exception("23505")));
        Assert.AreEqual(DatabaseFailureKind.Unknown, classifier.Classify(new DBConcurrencyException()));
    }

    /// <summary>实体与仓储共用 PG 数量映射，查询别名可由真实 Dapper 还原属性。</summary>
    [TestMethod]
    public void Mapping_UsesOnePgMappingAndDapperAliases()
    {
        var pg = EntityMetadataCache.GetMetadata(typeof(PurchaseOrderLine));
        Assert.AreSame(pg, EntityMetadataCache.GetMetadata<PurchaseOrderLine>());
        Assert.AreEqual("quantity", pg.GetProperty("Quantity").ColumnName);
        Assert.AreEqual("Quantity", pg.GetProperty("Quantity").ParameterName);
        Assert.AreEqual("purchase_order_line", pg.TableName);
        var row = new DataTable();
        row.Columns.Add(pg.GetProperty("Quantity").PropertyName, typeof(decimal));
        row.Rows.Add(12.5m);
        using var reader = row.CreateDataReader();
        var parser = reader.GetRowParser<PurchaseOrderLine>();
        Assert.IsTrue(reader.Read());
        Assert.AreEqual(12.5m, parser(reader).Quantity);
        var inherited = EntityMetadataCache.GetMetadata(typeof(NameExample));
        Assert.AreEqual("uid", inherited.KeyName);
        Assert.AreEqual(8, inherited.Columns.Count);
        Assert.AreEqual("stored_path", inherited.GetProperty("IMPath").ColumnName);
    }

    /// <summary>缩写和长名称稳定处理；转换后的重名必须阻断生成。</summary>
    [TestMethod]
    public void Names_AreStableAndRejectCollisions()
    {
        Assert.AreEqual("product_sku", PostgreSqlNames.FromClr("ProductSKU"));
        Assert.AreEqual("length_cm", PostgreSqlNames.FromClr("Length_CM"));
        var name = new string('a', 80);
        Assert.AreEqual(63, PostgreSqlNames.FromClr(name).Length);
        Assert.AreEqual(PostgreSqlNames.FromClr(name), PostgreSqlNames.FromClr(name));
        Assert.AreNotEqual(PostgreSqlNames.FromClr(name), PostgreSqlNames.FromClr(name + "b"));
        Assert.Throws<InvalidOperationException>(() => EntityMetadataCache.GetMetadata(typeof(CollisionExample)));
    }

    /// <summary>独立供应商资料允许空SKU，部分唯一索引只限制已关联的活动关系。</summary>
    [TestMethod]
    public void SupplierSku_IsNullableWithPartialUniqueIndex()
    {
        var script = Generate();
        var table = Regex.Match(script, "CREATE TABLE \"public\".\"supplier_product\" \\((.*?)\\n\\);", RegexOptions.Singleline).Value;
        StringAssert.Contains(table, "\"sku_id\" uuid NULL");
        StringAssert.Contains(script, "ON \"public\".\"supplier_product\" (\"supplier_id\", \"sku_id\") WHERE \"is_deleted\" = FALSE AND \"sku_id\" IS NOT NULL;");
        Assert.IsTrue(EntityColumnSchema.IsNullable(typeof(SupplierProduct).GetProperty(nameof(SupplierProduct.SkuId))!));
    }

    /// <summary>客户供应商仅活动编码索引采用已批准的 ICU 表达式，不扩大列级比较或停用过滤。</summary>
    [TestMethod]
    [DataRow("customer", "customer_code")]
    [DataRow("supplier", "supplier_code")]
    public void PartyCodeIndex_UsesApprovedExpressionWithoutChangingColumns(string tableName, string codeName)
    {
        var script = Generate();
        StringAssert.Contains(script, $"CREATE UNIQUE INDEX \"ux_{tableName}_{codeName}\" ON \"public\".\"{tableName}\" ((rtrim(\"{codeName}\", ' ') COLLATE public.erp_dictionary_ci)) WHERE \"is_deleted\" = FALSE;");
        var table = Regex.Match(script, $"CREATE TABLE \"public\".\"{tableName}\" \\((.*?)\\n\\);", RegexOptions.Singleline).Value;
        Assert.IsFalse(string.IsNullOrEmpty(table));
        Assert.IsFalse(table.Contains("COLLATE", StringComparison.Ordinal));
    }

    /// <summary>分类可空编码及颜色唯一编码仅更改索引比较，保留NULL过滤且不扩大到普通颜色码或列定义。</summary>
    [TestMethod]
    [DataRow("product_category", "category_code", true)]
    [DataRow("product_color", "color_code_unique", false)]
    public void ClassificationCodeIndex_PreservesNullAndColumnBoundaries(string tableName, string codeName, bool nullable)
    {
        var script = Generate();
        var nullFilter = nullable ? $" AND \"{codeName}\" IS NOT NULL" : "";
        StringAssert.Contains(script, $"CREATE UNIQUE INDEX \"ux_{tableName}_{codeName}\" ON \"public\".\"{tableName}\" ((rtrim(\"{codeName}\", ' ') COLLATE public.erp_dictionary_ci)) WHERE \"is_deleted\" = FALSE{nullFilter};");
        var table = Regex.Match(script, $"CREATE TABLE \"public\".\"{tableName}\" \\((.*?)\\n\\);", RegexOptions.Singleline).Value;
        Assert.IsFalse(string.IsNullOrEmpty(table));
        Assert.IsFalse(table.Contains("COLLATE", StringComparison.Ordinal));
        Assert.IsFalse(script.Contains("rtrim(\"color_code\"", StringComparison.Ordinal));
        var property = nullable ? typeof(ProductCategory).GetProperty(nameof(ProductCategory.CategoryCode))!
            : typeof(ProductColor).GetProperty(nameof(ProductColor.ColorCodeUnique))!;
        Assert.AreEqual(nullable, EntityColumnSchema.IsNullable(property));
    }

    /// <summary>型号和中性编码仅改变指定唯一键，不改变非空列、父颜色组合唯一或列级比较。</summary>
    [TestMethod]
    [DataRow("product_master", "master_sku")]
    [DataRow("product_neutral", "product_code")]
    public void ProductCodeIndex_PreservesColumnAndCompositeRules(string tableName, string codeName)
    {
        var script = Generate();
        StringAssert.Contains(script, $"CREATE UNIQUE INDEX \"ux_{tableName}_{codeName}\" ON \"public\".\"{tableName}\" ((rtrim(\"{codeName}\", ' ') COLLATE public.erp_dictionary_ci)) WHERE \"is_deleted\" = FALSE;");
        var table = Regex.Match(script, $"CREATE TABLE \"public\".\"{tableName}\" \\((.*?)\\n\\);", RegexOptions.Singleline).Value;
        StringAssert.Contains(table, $"\"{codeName}\" varchar(300) NOT NULL");
        Assert.IsFalse(table.Contains("COLLATE", StringComparison.Ordinal));
        StringAssert.Contains(script, "CREATE UNIQUE INDEX \"ux_product_neutral_master_color\" ON \"public\".\"product_neutral\" (\"product_master_id\", \"color_id\") WHERE \"is_deleted\" = FALSE;");
        Assert.IsFalse(script.Contains("rtrim(\"master_sku\", ' ') COLLATE public.erp_dictionary_ci)) WHERE \"is_active\"", StringComparison.Ordinal));
    }

    /// <summary>持久化声明直接使用 PG 表达式及布尔默认值，不再依赖源方言翻译。</summary>
    [TestMethod]
    public void Expressions_UseNativePgDeclarations()
    {
        var checks = typeof(BaseEntity).Assembly.GetTypes()
            .SelectMany(type => type.GetCustomAttributes<DbCheckAttribute>(true));
        Assert.IsTrue(checks.All(check => !check.Expression.Contains('[')));
        Assert.AreEqual("FALSE", typeof(BaseEntity).GetProperty(nameof(BaseEntity.IsDeleted))!
            .GetCustomAttribute<DbColumnAttribute>()!.DefaultExpression);
        StringAssert.Contains(Generate(), "\"quantity\" > 0");
        StringAssert.Contains(Generate(), "\"is_deleted\" = FALSE");
    }

    /// <summary>未知类型和附带 SQL 的类型声明必须拒绝，不能被当作原生类型直接生成。</summary>
    [TestMethod]
    [DataRow(nameof(InvalidTypeExample.LegacyType))]
    [DataRow(nameof(InvalidTypeExample.InjectedType))]
    public void Types_RejectUnsupportedDeclarations(string property)
        => Assert.Throws<InvalidOperationException>(() => EntityColumnSchema.ResolveStoreType(
            typeof(InvalidTypeExample).GetProperty(property)!));

    /// <summary>全实体结构确定性生成，包含原保护规则；JSON不能放宽为标量。</summary>
    [TestMethod]
    public void Schema_IsDeterministicAndComplete()
    {
        var script = Generate();
        Assert.AreEqual(script, Generate());
        Assert.AreEqual(50, Regex.Matches(script, "CREATE TABLE ").Count);
        Assert.AreEqual(1076, Regex.Matches(script, "COMMENT ON COLUMN ").Count);
        Assert.AreEqual(110, Regex.Matches(script, " CHECK ").Count);
        Assert.AreEqual(58, Regex.Matches(script, "CREATE UNIQUE INDEX ").Count);
        Assert.AreEqual(10, Regex.Matches(script, "CREATE TRIGGER ").Count);
        StringAssert.Contains(script, "IS JSON OBJECT OR \"result_json\" IS JSON ARRAY");
        StringAssert.Contains(script, "length(rtrim(\"command_type\", ' '))");
        StringAssert.Contains(script, "\"recorded_at_utc\" timestamp(6) with time zone");
        StringAssert.Contains(script, "IS DISTINCT FROM OLD.");
        StringAssert.Contains(script, "目标数据库不是空库");
        Assert.IsFalse(script.Contains("FOREIGN KEY", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("WITH (UPDLOCK", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(script.Contains("CREATE DATABASE", StringComparison.OrdinalIgnoreCase));
    }

    private static string Generate() => new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);

    /// <summary>继承、Column例外及忽略字段的测试模型。</summary>
    private sealed class NameExample : BaseEntity
    {
        [Column("stored_path")] public string? IMPath { get; set; }
        [NotMapped] public string? Display { get; set; }
    }

    /// <summary>用于证明物理列重名会被拒绝。</summary>
    private sealed class CollisionExample : BaseEntity
    {
        public string? SKU { get; set; }
        public string? Sku { get; set; }
    }

    /// <summary>类型声明负例只用于离线验证，不进入生产实体或生成结构。</summary>
    private sealed class InvalidTypeExample
    {
        [Column(TypeName = "nvarchar(100)")] public string LegacyType { get; set; } = "";
        [Column(TypeName = "text; DROP TABLE x")] public string InjectedType { get; set; } = "";
    }
}

using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Text.RegularExpressions;
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
    /// <summary>工厂只构造Npgsql连接，既有SQL Server工厂保持原提供者。</summary>
    [TestMethod]
    public void Factories_CreateClosedConnections()
    {
        var factory = new PostgreSqlConnectionFactory("Host=localhost;Database=unit_test;Username=test");
        using var connection = factory.CreateConnection();
        Assert.IsInstanceOfType<NpgsqlConnection>(connection);
        Assert.AreEqual(ConnectionState.Closed, connection.State);
        Assert.AreSame(PostgreSqlExceptionClassifier.Instance, factory.Errors);
        using var legacy = new SqlConnectionFactory("Server=localhost;Database=unit_test;Integrated Security=true").CreateConnection();
        Assert.IsInstanceOfType<Microsoft.Data.SqlClient.SqlConnection>(legacy);
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

    /// <summary>物理数量列映射不污染旧QTY，查询别名可由真实Dapper还原属性。</summary>
    [TestMethod]
    public void Mapping_PreservesLegacyAndDapperAliases()
    {
        var pg = PostgreSqlEntityMetadata.GetMetadata(typeof(PurchaseOrderLine));
        Assert.AreEqual("QTY", EntityMetadataCache.GetMetadata<PurchaseOrderLine>().GetProperty("Quantity").ColumnName);
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
        var inherited = PostgreSqlEntityMetadata.GetMetadata(typeof(NameExample));
        Assert.AreEqual("uid", inherited.KeyName);
        Assert.AreEqual(8, inherited.Columns.Count);
        Assert.AreEqual("im_path", inherited.GetProperty("IMPath").ColumnName);
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
        Assert.Throws<InvalidOperationException>(() => PostgreSqlEntityMetadata.GetMetadata(typeof(CollisionExample)));
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

    /// <summary>仅转换已知行内谓词，保持字面量完整；未知SQL不能静默穿透。</summary>
    [TestMethod]
    public void Expressions_KeepLiteralsAndRejectUnknownSql()
    {
        var result = PostgreSqlSchemaExpressions.Convert(typeof(PurchaseOrderLine), "[QTY] > 0 AND [IsDeleted] = 0");
        StringAssert.Contains(result, "\"quantity\" > 0");
        StringAssert.Contains(result, "\"is_deleted\" = FALSE");
        StringAssert.Contains(PostgreSqlSchemaExpressions.Convert(typeof(NameExample), "[stored_path] = N'LEN([QTY])'"), "'LEN([QTY])'");
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSchemaExpressions.Convert(typeof(NameExample), "EXISTS (SELECT 1)"));
        Assert.Throws<InvalidOperationException>(() => PostgreSqlSchemaExpressions.Convert(typeof(NameExample), "[stored_path] IS NULL; DROP TABLE x"));
    }

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
}

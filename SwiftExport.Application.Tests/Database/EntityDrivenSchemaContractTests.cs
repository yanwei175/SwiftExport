using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Infrastructure.DatabaseSchema;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;

namespace SwiftExport.Application.Tests.Database;

/// <summary>验证实体是数据库结构的唯一事实源，生成基线不包含数据库外键。</summary>
[TestClass]
public sealed class EntityDrivenSchemaContractTests
{
    [TestMethod]
    public void PersistenceEntities_MustHaveUniqueExplicitTableMappings()
    {
        var entities = PersistentEntities();
        Assert.IsNotEmpty(entities);
        Assert.IsTrue(entities.All(type => type.GetCustomAttribute<TableAttribute>() is not null));
        Assert.IsEmpty(entities.GroupBy(type => type.GetCustomAttribute<TableAttribute>()!.Name, StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1).ToArray());
    }

    [TestMethod]
    public void SchemaGenerator_MustBeDeterministicAndContainNoForeignKeys()
    {
        var first = Generate();
        Assert.AreEqual(first, Generate());
        Assert.IsFalse(Regex.IsMatch(first, @"\bFOREIGN\s+KEY\b|\bREFERENCES\b", RegexOptions.IgnoreCase));
        StringAssert.Contains(first, "CREATE SEQUENCE [dbo].[InventoryLedgerSequence]");
        StringAssert.Contains(first, "CREATE TABLE [dbo].[DemandExecutionLink]");
        StringAssert.Contains(first, "CREATE TABLE [dbo].[ExcelFieldMapping]");
        StringAssert.Contains(first, "CREATE UNIQUE INDEX [UX_ExcelFieldMapping_Group_Header]");
        StringAssert.Contains(first, "[NormalizedHeader] <> N''");
        StringAssert.Contains(first, "@name=N'MS_Description'");
        StringAssert.Contains(first, "@level1type=N'TABLE', @level1name=N'Customer';");
        StringAssert.Contains(first, "@level2type=N'COLUMN', @level2name=N'CustomerCode';");
        StringAssert.Contains(first, "@value=N'客户主数据实体，保存客户身份、联系方式及资料目录配置。'");

        var expectedDescriptions = PersistentEntities()
            .Sum(entity => 1 + MappedProperties(entity).Count);
        Assert.AreEqual(expectedDescriptions,
            Regex.Matches(first, "@name=N'MS_Description'", RegexOptions.CultureInvariant).Count);
    }

    [TestMethod]
    public void CommittedBaseline_MustMatchEntityMetadataAndKeepDataCopySeparate()
    {
        var root = Root();
        var path = Path.Combine(root, "SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");
        Assert.AreEqual(Normalize(Generate()), Normalize(File.ReadAllText(path)));

        var generatedDirectory = Path.GetDirectoryName(path)!;
        var files = Directory.EnumerateFiles(generatedDirectory, "*.sql", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(generatedDirectory, file))
            .ToArray();
        CollectionAssert.AreEqual(new[] { "SwiftExport.Schema.sql" }, files);
        var copyPath = Path.Combine(root, "SwiftExport.Infrastructure", "Database", "主数据跨库复制.sql");
        Assert.IsTrue(File.Exists(copyPath));
        Assert.IsFalse(Path.GetFullPath(copyPath).StartsWith(Path.GetFullPath(generatedDirectory) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(File.ReadAllText(path).Contains("SysUser_ModulePermissions", StringComparison.Ordinal));
    }

    /// <summary>主数据复制脚本只包含批准的主数据、账号和权限关系，并保留空目标及事务保护。</summary>
    [TestMethod]
    public void MasterDataCopyScript_MustFreezeApprovedTablesAndSafetyGuards()
    {
        var path = Path.Combine(Root(), "SwiftExport.Infrastructure", "Database", "主数据跨库复制.sql");
        var sql = File.ReadAllText(path);
        var tables = Regex.Matches(sql, @"\(\d+,\s+N'([^']+)'\)")
            .Select(match => match.Groups[1].Value)
            .ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "ProductCategory", "ProductColor", "Supplier", "Customer", "SysDicCurrency",
            "SysDicCurrencyExchange", "SysDictType", "SysDictData", "ExcelFieldMapping", "SysRoles", "SysUser",
            "ProductMaster", "Product", "ProductNeutral", "ProductDetails",
            "ProductPackage", "Bom", "CustomerPoConfig", "CustomerProduct", "SupplierProduct",
            "WarehouseSite", "SysPermissionDefinitions", "SysRole_Permissions", "SysUser_Roles"
        }, tables);
        StringAssert.Contains(sql, "目标表 dbo.");
        StringAssert.Contains(sql, "BEGIN TRANSACTION");
        StringAssert.Contains(sql, "ROLLBACK TRANSACTION");
        StringAssert.Contains(sql, "@MissingColumns");
        StringAssert.Contains(sql, "#IntegrityIssues");
        StringAssert.Contains(sql, "SysPermissionDefinitions.PermissionKey->Uid");
        Assert.IsFalse(sql.Contains("Product.CategoryId", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("WITHIN GROUP", StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(sql, "MigrationStage");
        StringAssert.Contains(sql, "SqlErrorMessage");
        StringAssert.Contains(sql, "THROW 52212, @ErrorMessage, 1");
        StringAssert.Contains(sql, "动态SQL=");
        StringAssert.Contains(sql, "@IntegritySummary nvarchar(max)");
        StringAssert.Contains(sql, "RelationName + N'=' + CONVERT(nvarchar(30), InvalidRows)");
        Assert.IsFalse(sql.Contains("THROW 52211", StringComparison.Ordinal));
        StringAssert.Contains(sql, "主数据跨库复制成功，但存在无效逻辑引用");
        StringAssert.Contains(sql, "IntegrityWarnings");
        Assert.IsTrue(sql.IndexOf("SELECT RelationName,InvalidRows", StringComparison.Ordinal)
            > sql.IndexOf("COMMIT TRANSACTION;", StringComparison.Ordinal));
        StringAssert.Contains(sql, ".dbo.[Customer] p ON p.Uid=x.CustomerId");
        Assert.IsFalse(Regex.IsMatch(sql, @"\.dbo\.(Customer|Product|Supplier)\b", RegexOptions.CultureInvariant));
        StringAssert.Contains(sql, "DECLARE @SourceQuoted nvarchar(max)");
        StringAssert.Contains(sql, "DECLARE @TargetQuoted nvarchar(max)");
        Assert.IsFalse(sql.Contains("@SourceQuoted nvarchar(258)", StringComparison.Ordinal));
        Assert.IsFalse(sql.Contains("@TargetQuoted nvarchar(258)", StringComparison.Ordinal));
        Assert.AreEqual(3, Regex.Matches(sql, @"DROP TABLE IF EXISTS #MigrationCounts;", RegexOptions.CultureInvariant).Count);
        Assert.AreEqual(3, Regex.Matches(sql, @"DROP TABLE IF EXISTS #IntegrityIssues;", RegexOptions.CultureInvariant).Count);
    }

    [TestMethod]
    public void DemandExecutionLink_MustExposeOnlyRemainingContributionSemantics()
    {
        var type = typeof(DemandExecutionLink);
        Assert.IsNull(type.GetProperty("NetDemandAllocationId"));
        Assert.IsNotNull(type.GetCustomAttributes<DbCheckAttribute>().SingleOrDefault(item => item.Name == "CK_DemandExecutionLink_Quantity"));
        Assert.IsNotNull(type.GetCustomAttributes<DbCheckAttribute>().SingleOrDefault(item => item.Name == "CK_DemandExecutionLink_State"));
        Assert.IsNotNull(type.GetCustomAttributes<DbCheckAttribute>().SingleOrDefault(item => item.Name == "CK_DemandExecutionLink_NotDeleted"));
        Assert.IsTrue(type.GetCustomAttributes<PolymorphicLogicalReferenceAttribute>().All(item => item.DiscriminatorValue == "PO"));
    }

    [TestMethod]
    public void RetiredPersistenceTypes_MustNotExist()
    {
        var names = typeof(BaseEntity).Assembly.GetTypes().Select(type => type.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var removed in new[] { "NetDemandAllocation", "NetDemandPlanCommandLog", "IndependentDemand", "DependentDemand" })
            Assert.IsFalse(names.Contains(removed), removed);
    }

    private static Type[] PersistentEntities() => typeof(BaseEntity).Assembly.GetTypes()
        .Where(type => !type.IsAbstract && typeof(BaseEntity).IsAssignableFrom(type))
        .OrderBy(type => type.FullName, StringComparer.Ordinal)
        .ToArray();

    /// <summary>按生成器规则取得实体映射字段，用于断言每张表及每个字段均生成说明。</summary>
    private static IReadOnlyList<PropertyInfo> MappedProperties(Type entity)
    {
        var hierarchy = new Stack<Type>();
        for (var current = entity; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Push(current);
        return hierarchy.SelectMany(type => type.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite
                && property.GetCustomAttribute<NotMappedAttribute>() is null)
            .OrderBy(property => property.MetadataToken))
            .ToArray();
    }

    private static string Generate() => new SqlServerSchemaGenerator().Generate(typeof(BaseEntity).Assembly);

    private static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd();

    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }
}

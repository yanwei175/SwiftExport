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

    /// <summary>PG 结构生成必须确定、无数据库外键，并完整保存实体及字段中文说明。</summary>
    [TestMethod]
    public void SchemaGenerator_MustBeDeterministicAndContainNoForeignKeys()
    {
        var first = Generate();
        Assert.AreEqual(first, Generate());
        Assert.IsFalse(Regex.IsMatch(first, @"\bFOREIGN\s+KEY\b|\bREFERENCES\b", RegexOptions.IgnoreCase));
        StringAssert.Contains(first, "CREATE SEQUENCE public.\"inventory_ledger_sequence\"");
        StringAssert.Contains(first, "CREATE TABLE \"public\".\"demand_execution_link\"");
        Assert.IsFalse(first.Contains("\"excel_field_mapping\"", StringComparison.Ordinal));
        StringAssert.Contains(first, "CREATE TABLE \"public\".\"excel_read_profile\"");
        StringAssert.Contains(first, "CREATE TABLE \"public\".\"excel_read_field\"");
        StringAssert.Contains(first, "COMMENT ON ");
        StringAssert.Contains(first, "COMMENT ON TABLE \"public\".\"customer\" IS");
        StringAssert.Contains(first, "COMMENT ON COLUMN \"public\".\"customer\".\"customer_code\" IS");
        StringAssert.Contains(first, "IS '客户主数据实体，保存客户身份、联系方式及资料目录配置。'");

        var expectedDescriptions = PersistentEntities()
            .Sum(entity => 1 + MappedProperties(entity).Count);
        Assert.AreEqual(expectedDescriptions,
            Regex.Matches(first, "^COMMENT ON (?:TABLE|COLUMN) ", RegexOptions.CultureInvariant | RegexOptions.Multiline).Count);
    }

    /// <summary>当前PG基线与元数据完全一致；源夹具已退出，生成目录仅保留PG基线。</summary>
    [TestMethod]
    public void CommittedBaseline_MustMatchEntityMetadata()
    {
        var root = Root();
        var path = Path.Combine(root, "SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.PostgreSql.Schema.preview.sql");
        Assert.AreEqual(Normalize(Generate()), Normalize(File.ReadAllText(path)));

        var generatedDirectory = Path.GetDirectoryName(path)!;
        var files = Directory.EnumerateFiles(generatedDirectory, "*.sql", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(generatedDirectory, file))
            .ToArray();
        CollectionAssert.AreEquivalent(new[] { "SwiftExport.PostgreSql.Schema.preview.sql" }, files);
        Assert.AreEqual(Normalize(new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly)),
            Normalize(File.ReadAllText(Path.Combine(generatedDirectory, "SwiftExport.PostgreSql.Schema.preview.sql"))));
        Assert.IsFalse(File.ReadAllText(path).Contains("SysUser_ModulePermissions", StringComparison.Ordinal));
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
        foreach (var removed in new[] { "ExcelFieldMapping", "NetDemandAllocation", "NetDemandPlanCommandLog", "IndependentDemand", "DependentDemand" })
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

    /// <summary>只生成当前 PostgreSQL 结构评审文本，不连接或部署数据库。</summary>
    private static string Generate() => new PostgreSqlSchemaGenerator().Generate(typeof(BaseEntity).Assembly);

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

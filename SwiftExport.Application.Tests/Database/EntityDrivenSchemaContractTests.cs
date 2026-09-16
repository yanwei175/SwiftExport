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
    }

    [TestMethod]
    public void CommittedBaseline_MustMatchEntityMetadataAndBeTheOnlySqlFile()
    {
        var root = Root();
        var path = Path.Combine(root, "SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql");
        Assert.AreEqual(Normalize(Generate()), Normalize(File.ReadAllText(path)));

        var files = Directory.EnumerateFiles(root, "*.sql", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Select(file => Path.GetRelativePath(root, file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();
        CollectionAssert.AreEqual(new[] { Path.Combine("SwiftExport.Infrastructure", "Database", "Generated", "SwiftExport.Schema.sql") }, files);
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

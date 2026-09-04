using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Shipping;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.RegularExpressions;

namespace SwiftExport.Application.Tests.Database;

[TestClass]
public sealed class TargetDatabaseBaselineContractTests
{
    private static readonly Dictionary<Type, string> TargetMappings = new()
    {
        [typeof(GrossDemand)] = "GrossDemand",
        [typeof(NetDemand)] = "NetDemand",
        [typeof(ProductMaster)] = "ProductMaster",
        [typeof(ProductNeutral)] = "ProductNeutral",
        [typeof(SupplierProgressReport)] = "SupplierProgressReport",
        [typeof(WarehouseSite)] = "WarehouseSite",
        [typeof(InventoryBalance)] = "InventoryBalance",
        [typeof(InventoryLedgerEntry)] = "InventoryLedgerEntry",
        [typeof(InboundDocument)] = "InboundDocument",
        [typeof(InboundDocumentLine)] = "InboundDocumentLine",
        [typeof(OutboundDocument)] = "OutboundDocument",
        [typeof(OutboundDocumentLine)] = "OutboundDocumentLine",
        [typeof(PackingListDocument)] = "PackingListDocument",
        [typeof(PackingListContributionLine)] = "PackingListContributionLine",
        [typeof(SalesOrderLine)] = "SalesOrderLine"
    };

    [TestMethod]
    public void Entities_MustMapToTargetPhysicalTables()
    {
        foreach (var (entityType, expectedTable) in TargetMappings)
            Assert.AreEqual(expectedTable, entityType.GetCustomAttributes(typeof(TableAttribute), false).Cast<TableAttribute>().Single().Name, entityType.Name);
    }

    [TestMethod]
    public void PersistenceEntities_MustNotContainRemovedCompatibilityFacts()
    {
        AssertMissing<NetDemand>("PreferredSupplierId", "PreferredSupplierCode");
        AssertMissing<DemandExecutionLink>("CompletedQty", "ReceivedQty");
        AssertMissing<SalesOrderLine>("LastShippedDate");
        AssertMissing<PackingListDocument>("LegacyHeadId", "IsLegacyReadOnly", "ReadOnlyReason");
        AssertMissing<PackingListContributionLine>("LegacyPackingListHeadId");
        Assert.AreEqual(typeof(Guid), typeof(DemandExecutionLink).GetProperty(nameof(DemandExecutionLink.NetDemandAllocationId))!.PropertyType);
        Assert.AreEqual(typeof(Guid), typeof(DemandExecutionLink).GetProperty(nameof(DemandExecutionLink.CommandItemId))!.PropertyType);
    }

    [TestMethod]
    public void InventoryEnumValues_AreFrozen()
    {
        CollectionAssert.AreEqual(new[] { 0, 8, 9 }, Enum.GetValues<InventoryDocumentLifecycleStatusEnum>().Select(x => (int)x).ToArray());
        CollectionAssert.AreEqual(new[] { 0 }, Enum.GetValues<WarehouseSiteTypeEnum>().Select(x => (int)x).ToArray());
    }

    [TestMethod]
    public void ActiveRepositorySql_MustNotReferenceRetiredPhysicalTables()
    {
        var oldNames = new[] { "IndependentDemand", "DependentDemand", "FollowUpLog", "PurchaseReceiveHead", "PurchaseReceiveLine", "PackingListHead", "PackingListLine", "Warehouse", "Inventory", "InventoryTransaction", "OutboundHead", "OutboundLine" };
        var violations = new List<string>();
        var repositoryRoot = Path.Combine(Root(), "SwiftExport.Infrastructure", "Repositories");
        foreach (var path in Directory.EnumerateFiles(repositoryRoot, "*.cs", SearchOption.AllDirectories))
        {
            var source = File.ReadAllText(path);
            foreach (var oldName in oldNames)
            {
                var pattern = $@"(?:dbo\s*\.\s*{Regex.Escape(oldName)}\b|\[dbo\]\s*\.\s*\[{Regex.Escape(oldName)}\])";
                if (Regex.IsMatch(source, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                    violations.Add($"{Path.GetRelativePath(Root(), path)} -> {oldName}");
            }
        }
        Assert.IsEmpty(violations, string.Join(Environment.NewLine, violations));
    }

    [TestMethod]
    public void FreshInstallScript_MustInitializeOnlyNewTargetBusinessTables()
    {
        var script = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "030_初始化目标业务表.sql");
        StringAssert.Contains(script, "DECLARE @AllowInitializeNewDatabase bit=1");
        StringAssert.Contains(script, "DECLARE @ExpectedDatabaseName sysname=N'dv_next'");
        StringAssert.Contains(script, "IF EXISTS(SELECT 1 FROM sys.tables WHERE is_ms_shipped=0)");
        StringAssert.Contains(script, "BEGIN TRAN;");
        StringAssert.Contains(script, "BEGIN CATCH");
        StringAssert.Contains(script, "IF @@TRANCOUNT<>0");
        foreach (var table in new[] { "SupplierProduct", "ProductMaster", "ProductNeutral", "ProductPackage", "Bom", "SysDicCurrencyExchange", "DocumentSequence", "SalesOrderHeader", "SalesOrderLine", "GrossDemand", "NetDemand", "NetDemandAllocation", "NetDemandPlanCommandLog", "DemandExecutionLink", "PurchaseOrderHead", "PurchaseOrderLine", "SupplierProgressReport", "WarehouseSite", "InventoryBalance", "InventoryLedgerEntry", "InboundDocument", "InboundDocumentLine", "OutboundDocument", "OutboundDocumentLine", "PackingListDocument", "PackingListContributionLine", "BusinessOperationLog" })
            StringAssert.Contains(script, $"CREATE TABLE dbo.{table}", table);
        foreach (var exception in new[] { "CustomerProduct", "CustomerPoConfig", "Customer", "Product", "ProductCategory", "ProductColor", "ProductDetails", "Supplier", "SysDicCurrency", "SysDictData", "SysDictType", "SysRoles", "SysUser", "SysUser_Roles", "SysViewRoute" })
            Assert.IsFalse(Regex.IsMatch(script, $@"DROP\s+TABLE(?:\s+IF\s+EXISTS)?\s+dbo\.{Regex.Escape(exception)}\b", RegexOptions.IgnoreCase), exception);
        foreach (var excluded in new[] { "UserRoleView", "ProductSearchMeta", "AccessControlList", "ExcelImportConfig", "SysPermission", "SysRolePermission" })
            Assert.IsFalse(script.Contains($"CREATE TABLE dbo.{excluded}", StringComparison.OrdinalIgnoreCase), excluded);
        Assert.IsFalse(Regex.IsMatch(script, @"\b(?:DROP\s+TABLE|DELETE\s+FROM|TRUNCATE\s+TABLE)\b", RegexOptions.IgnoreCase));
    }

    [TestMethod]
    public void RebuildArtifacts_MustContainReadOnlyChecksAndNoEmbeddedDbSeedSecrets()
    {
        var preflight = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "010_源库只读预检.sql");
        var postflight = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "070_迁移后只读检查.sql");
        Assert.IsFalse(Regex.IsMatch(preflight, @"(?:UPDATE|DELETE|ALTER|DROP|CREATE)\s+dbo\.", RegexOptions.IgnoreCase));
        Assert.IsFalse(Regex.IsMatch(postflight, @"(?:INSERT|UPDATE|DELETE|ALTER|DROP|CREATE)\s+dbo\.", RegexOptions.IgnoreCase));
        StringAssert.Contains(preflight, "目标 ProductId 将保持 NULL");
        StringAssert.Contains(preflight, "目标 SupplierId 将保持 NULL");
        var seed = Read("_tools", "DbSeed", "Program.cs");
        StringAssert.Contains(seed, "SWIFTEXPORT_DBSEED_CONNECTION");
        StringAssert.Contains(seed, "SWIFTEXPORT_DBSEED_PASSWORD_HASH");
        Assert.IsFalse(seed.Contains("Password=", StringComparison.OrdinalIgnoreCase));
        Assert.IsFalse(seed.Contains("CREATE TABLE", StringComparison.OrdinalIgnoreCase));
    }

    [TestMethod]
    public void FreshInstallArtifacts_MustBeGuardedAndKeepSourceReadOnly()
    {
        var preflight = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "010_源库只读预检.sql");
        Assert.IsFalse(Regex.IsMatch(preflight, @"\)d\s*;", RegexOptions.IgnoreCase), "重复项汇总为 0 时不得输出 BLOCKER。");

        var createDatabase = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "020_创建新数据库.sql");
        StringAssert.Contains(createDatabase, "DECLARE @AllowCreateDatabase bit=1");
        StringAssert.Contains(createDatabase, "DECLARE @SourceDatabaseName sysname=N'dv'");
        StringAssert.Contains(createDatabase, "DECLARE @TargetDatabaseName sysname=N'dv_next'");
        StringAssert.Contains(createDatabase, "目标数据库已经存在；本脚本拒绝覆盖或复用");

        foreach (var migrationName in new[] { "040_迁移业务白名单.sql", "050_迁移系统白名单.sql" })
        {
            var migration = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", migrationName);
            StringAssert.Contains(migration, "DECLARE @AllowWhitelistMigration bit=1");
            StringAssert.Contains(migration, "DECLARE @SourceDatabaseName sysname=N'dv'");
            StringAssert.Contains(migration, "DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next'");
            StringAssert.Contains(migration, "SELECT * INTO dbo.");
            Assert.IsFalse(Regex.IsMatch(migration, @"(?:UPDATE|DELETE|ALTER|DROP|TRUNCATE)\s+" + @"\+?@sourcePrefix", RegexOptions.IgnoreCase), migrationName);
        }

        var enrichment = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "060_补充白名单关系与索引.sql");
        StringAssert.Contains(enrichment, "DECLARE @AllowTargetEnrichment bit=1");
        StringAssert.Contains(enrichment, "DECLARE @ExpectedTargetDatabaseName sysname=N'dv_next'");
        StringAssert.Contains(enrichment, "ProductId=p.Uid");
        StringAssert.Contains(enrichment, "p.MatchCount=1");
        StringAssert.Contains(enrichment, "ALTER TABLE dbo.Product ADD Unit nvarchar(100) NULL");
        StringAssert.Contains(enrichment, "ALTER TABLE dbo.CustomerProduct DROP COLUMN CustomerSkuUnit");
        StringAssert.Contains(enrichment, "ALTER TABLE dbo.CustomerProduct DROP COLUMN Currency");

        var postflight = Read("SwiftExport.Infrastructure", "Database", "FreshInstall", "070_迁移后只读检查.sql");
        StringAssert.Contains(postflight, "Product.Unit 必须为 nullable nvarchar(100)");
        StringAssert.Contains(postflight, "Product.Unit 不在启用的 Unit 字典中");
        StringAssert.Contains(postflight, "CustomerProduct 不得保留 CustomerSkuUnit 或 Currency");

        var additiveMigration = Read("SwiftExport.Infrastructure", "Migrations", "016_ProductUnit_AdditiveSchema.sql");
        StringAssert.Contains(additiveMigration, "ALTER TABLE dbo.Product ADD Unit nvarchar(100) NULL");
        Assert.IsFalse(additiveMigration.Contains("CustomerProduct", StringComparison.OrdinalIgnoreCase)
                       && Regex.IsMatch(additiveMigration, @"\b(?:UPDATE|ALTER)\s+(?:dbo\.)?CustomerProduct\b", RegexOptions.IgnoreCase));

        var retirementMigration = Read(
            "SwiftExport.Infrastructure",
            "Migrations",
            "017_CustomerProduct_RemoveLegacyUnitCurrency.sql");
        StringAssert.Contains(retirementMigration, "DROP COLUMN");
        StringAssert.Contains(retirementMigration, "CustomerSkuUnit");
        StringAssert.Contains(retirementMigration, "Currency");
        Assert.IsFalse(
            Regex.IsMatch(retirementMigration, @"\b(?:UPDATE|ALTER)\s+(?:dbo\.)?Product\b", RegexOptions.IgnoreCase),
            "客户产品遗留字段退役不得修改 Product 或回填 Product.Unit。");

        var dictionaryMigration = Read(
            "SwiftExport.Infrastructure",
            "Migrations",
            "019_SystemDictionary_Normalization.sql");
        StringAssert.Contains(dictionaryMigration, "N'ShippedBy'");
        StringAssert.Contains(dictionaryMigration, "N'PaymentMethod'");
        StringAssert.Contains(dictionaryMigration, "N'Incoterm'");
        StringAssert.Contains(dictionaryMigration, "DELETE FROM dbo.SysDictData");
        StringAssert.Contains(dictionaryMigration, "N'ProductCategory', N'ProductColor'");
        Assert.IsFalse(dictionaryMigration.Contains("dbo.SysDicCurrency", StringComparison.OrdinalIgnoreCase));

        var freshDictionary = Read(
            "SwiftExport.Infrastructure",
            "Database",
            "FreshInstall",
            "055_规范化系统字典.sql");
        StringAssert.Contains(freshDictionary, "DECLARE @AllowTargetDictionaryNormalization bit=1");
        StringAssert.Contains(freshDictionary, "N'ShippedBy'");
        StringAssert.Contains(postflight, "ProductCategory/ProductColor 不得保留通用字典记录");
        StringAssert.Contains(postflight, "客户与销售订单系统字典类型必须存在并受保护");

        var retiredReadme = Read("SwiftExport.Infrastructure", "Database", "Rebuild", "README.md");
        StringAssert.Contains(retiredReadme, "旧就地重建方案已停用");
        Assert.IsFalse(File.Exists(Path.Combine(Root(), "SwiftExport.Infrastructure", "Database", "Rebuild", "002_目标模型全量重建.sql")));
    }

    [TestMethod]
    public void ProductUnit_MustRemainNullableCurrentMasterDataInsteadOfReplacingSnapshots()
    {
        var property = typeof(Product).GetProperty(nameof(Product.Unit));
        Assert.IsNotNull(property);
        Assert.AreEqual(typeof(string), property.PropertyType);
        Assert.IsNotNull(typeof(InventoryBalance).GetProperty(nameof(InventoryBalance.UnitSnapshot)));
        Assert.IsNotNull(typeof(InventoryLedgerEntry).GetProperty(nameof(InventoryLedgerEntry.UnitSnapshot)));
    }

    private static void AssertMissing<T>(params string[] names)
    {
        var properties = typeof(T).GetProperties().Select(x => x.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var name in names) Assert.IsFalse(properties.Contains(name), $"{typeof(T).Name} 不应持久化 {name}。");
    }

    private static string Read(params string[] parts) => File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray()));
    private static string Root()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
        Assert.Fail("无法定位仓库根目录。");
        return string.Empty;
    }
}

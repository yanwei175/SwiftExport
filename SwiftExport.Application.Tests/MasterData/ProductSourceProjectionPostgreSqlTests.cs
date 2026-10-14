using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>物料自身型号与独立主型号关系的真实 PG 验收；临时结构和随机夹具均位于同一回滚事务。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductSourceProjectionPostgreSqlTests
{
    /// <summary>自由型号不依赖中性产品身份；空/失效主型号、可选编码匹配及明确清空在全部读取入口一致。</summary>
    [TestMethod]
    public async Task IndependentSources_ReadAndClearWithoutLegacyColumns()
    {
        var connectionString = Environment.GetEnvironmentVariable("SWIFTEXPORT_PG_ATTRIBUTE_TEST_CONNECTION");
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PG_ATTRIBUTE_TESTS") != "1"
            || string.IsNullOrWhiteSpace(connectionString))
            Assert.Inconclusive("未显式启用产品标签专用复制库测试。");
        var settings = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.AreEqual("192.168.3.12", settings.Host);
        Assert.AreEqual(5432, settings.Port);
        Assert.AreEqual("dv_next_product_attribute_tests_20261012", settings.Database);
        Assert.IsTrue(string.IsNullOrEmpty(settings.SearchPath) || settings.SearchPath == "public");

        using var session = new DbSession(new PostgreSqlConnectionFactory(connectionString!));
        Assert.AreEqual(settings.Database, await session.Connection.ExecuteScalarAsync<string>("SELECT current_database()"));
        Assert.AreEqual(settings.Host, await session.Connection.ExecuteScalarAsync<string>("SELECT host(inet_server_addr())"));
        Assert.AreEqual("5432", await session.Connection.ExecuteScalarAsync<string>("SELECT current_setting('port')"));
        await using var transaction = await ((NpgsqlConnection)session.Connection).BeginTransactionAsync();
        session.Transaction = transaction;
        try
        {
            // DDL 只在获批复制库当前事务内验证新查询；任何退出路径均回滚，不持久升级或清理数据库。
            await session.Connection.ExecuteAsync("""
                SET LOCAL lock_timeout='5s';
                ALTER TABLE public.product
                    DROP COLUMN IF EXISTS master_sku,
                    DROP COLUMN IF EXISTS product_neutral_id,
                    ADD COLUMN IF NOT EXISTS link_sku varchar(300) NULL;
                """, transaction: transaction);
            Assert.IsTrue(await session.Connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                    AND table_name='product' AND column_name='link_sku' AND data_type='character varying' AND is_nullable='YES')
                    AND NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                    AND table_name='product' AND column_name IN ('master_sku','product_neutral_id'))
                """, transaction: transaction));

            var user = new TestUser();
            var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var masters = new PostgreSqlDapperRepository<ProductMaster>(context);
            var neutrals = new PostgreSqlDapperRepository<ProductNeutral>(context);
            var products = new PostgreSqlProductRepository(context);
            var prefix = "SOURCE_" + Guid.NewGuid().ToString("N");
            var categories = new PostgreSqlDapperRepository<ProductCategory>(context);
            var categoryA = new ProductCategory { CategoryName = "English A", FakeName = "中文分类甲", CategoryCode = prefix + "_CA" };
            var categoryB = new ProductCategory { CategoryName = "English B", FakeName = "中文分类乙", CategoryCode = prefix + "_CB" };
            Assert.IsTrue((await categories.AddAsync(categoryA)).Success);
            Assert.IsTrue((await categories.AddAsync(categoryB)).Success);
            var masterA = new ProductMaster
            {
                MasterSku = prefix + "_A", CategoryId = categoryA.Uid, DefaultPcsPerPackage = 1,
                ProductAttributeProfileId = Guid.NewGuid()
            };
            var masterB = new ProductMaster
            {
                MasterSku = prefix + "_B", CategoryId = categoryB.Uid, DefaultPcsPerPackage = 1,
                ProductAttributeProfileId = Guid.NewGuid()
            };
            Assert.IsTrue((await masters.AddAsync(masterA)).Success);
            Assert.IsTrue((await masters.AddAsync(masterB)).Success);
            var neutralB = new ProductNeutral
            {
                ProductCode = prefix + "_NB", ProductMasterId = masterB.Uid, ColorId = Guid.NewGuid()
            };
            Assert.IsTrue((await neutrals.AddAsync(neutralB)).Success);
            var product = new Product
            {
                Sku = prefix + "_P", ProductMasterId = masterA.Uid, LinkSku = prefix + "_FREE",
                ColorId = Guid.NewGuid(), DescriptionEn = "物料独立描述", FeaturesZh = "物料独立配置",
                SupplyType = "采购", MaterialType = "成品", Unit = "PCS", UnitWeight = 2.75m, UnitCBM = 0.025m,
                BomNo = prefix + "_BOM", BomVersion = 1, Note = "来源变更保留其他资料"
            };
            Assert.IsTrue((await products.AddAsync(product)).Success);

            // 分页分类多选是并集，中文显示名仅属于本分页契约，不改其他读取入口的名称口径。
            var secondProduct = new Product { Sku = prefix + "_OTHER", ProductMasterId = masterB.Uid,
                LinkSku = prefix + "_SIDE", SupplyType = "采购", MaterialType = "成品", Unit = "PCS" };
            Assert.IsTrue((await products.AddAsync(secondProduct)).Success);
            var categoryPage = await products.GetPagedAsync(new ProductSearchRequest
            { Keyword = prefix, CategoryIds = [categoryA.Uid, categoryB.Uid, categoryA.Uid, Guid.Empty] });
            Assert.AreEqual(2, categoryPage.TotalCount);
            Assert.AreEqual(categoryA.FakeName, categoryPage.Items.Single(row => row.Uid == product.Uid).CategoryName);
            Assert.AreEqual(categoryB.FakeName, categoryPage.Items.Single(row => row.Uid == secondProduct.Uid).CategoryName);
            var singleCategory = await products.GetPagedAsync(new ProductSearchRequest
            { Keyword = prefix, CategoryIds = [categoryB.Uid] });
            Assert.AreEqual(secondProduct.Uid, singleCategory.Items.Single().Uid);
            Assert.AreEqual(categoryA.CategoryName, (await products.GetDtoByIdAsync(product.Uid))!.CategoryName);

            // 自由型号没有中性产品匹配也能读写和检索；模板始终取物料独立关联的主型号 A。
            await AssertReadsAsync(products, product, masterA.MasterSku, product.LinkSku,
                masterA.CategoryId, masterA.ProductAttributeProfileId);
            product.LinkSku = neutralB.ProductCode;
            Assert.IsTrue((await products.UpdateAsync(product)).Success);
            // 型号可恰好匹配 B 的中性产品编码，但不能改变主型号、分类、模板或物料自身颜色。
            await AssertReadsAsync(products, product, masterA.MasterSku, product.LinkSku,
                masterA.CategoryId, masterA.ProductAttributeProfileId);
            var originalModel = product.LinkSku;
            neutralB.ProductCode = prefix + "_RENAMED";
            Assert.IsTrue((await neutrals.UpdateAsync(neutralB)).Success);
            await AssertReadsAsync(products, product, masterA.MasterSku, originalModel,
                masterA.CategoryId, masterA.ProductAttributeProfileId);
            Assert.IsTrue((await neutrals.DeleteAsync(neutralB.Uid, neutralB.Version)).Success);
            await AssertReadsAsync(products, product, masterA.MasterSku, originalModel,
                masterA.CategoryId, masterA.ProductAttributeProfileId);

            foreach (var scenario in new[] { "master-only", "model-only", "empty", "missing", "master-and-free-model" })
            {
                var hasMaster = scenario is "master-only" or "master-and-free-model";
                product.ProductMasterId = hasMaster ? masterA.Uid
                    : scenario == "missing" ? Guid.NewGuid() : null;
                product.LinkSku = scenario is "master-only" or "empty" ? null : prefix + "_FREE";
                var oldVersion = product.Version;
                Assert.IsTrue((await products.UpdateAsync(product)).Success);
                Assert.AreEqual(oldVersion + 1, product.Version);
                await AssertReadsAsync(products, product,
                    hasMaster ? masterA.MasterSku : null, product.LinkSku,
                    hasMaster ? masterA.CategoryId : Guid.Empty,
                    hasMaster ? masterA.ProductAttributeProfileId : null);
            }

            product.ProductMasterId = masterA.Uid;
            product.LinkSku = prefix + "_FREE";
            Assert.IsTrue((await products.UpdateAsync(product)).Success);
            Assert.IsTrue((await masters.DeleteAsync(masterA.Uid, masterA.Version)).Success);
            await AssertReadsAsync(products, product, null, product.LinkSku, Guid.Empty, null);

            // 明确清空自身型号不应连带清除主型号身份、颜色、BOM或单品尺寸。
            product.LinkSku = null;
            Assert.IsTrue((await products.UpdateAsync(product)).Success);
            await AssertReadsAsync(products, product, null, null, Guid.Empty, null);
            var persisted = await products.GetByIdAsync(product.Uid, CancellationToken.None);
            Assert.IsNotNull(persisted);
            Assert.AreEqual(masterA.Uid, persisted.ProductMasterId);
            Assert.IsNull(persisted.LinkSku);
            Assert.AreEqual(product.DescriptionEn, persisted.DescriptionEn);
            Assert.AreEqual(product.FeaturesZh, persisted.FeaturesZh);
            Assert.AreEqual(product.ColorId, persisted.ColorId);
            Assert.AreEqual(product.UnitWeight, persisted.UnitWeight);
            Assert.AreEqual(product.UnitCBM, persisted.UnitCBM);
            Assert.AreEqual(product.BomNo, persisted.BomNo);
            Assert.AreEqual(product.BomVersion, persisted.BomVersion);
            Assert.AreEqual(product.Note, persisted.Note);
            product.ProductMasterId = null;
            Assert.IsTrue((await products.UpdateAsync(product)).Success);
            await AssertReadsAsync(products, product, null, null, Guid.Empty, null);
            Assert.IsNull((await products.GetByIdAsync(product.Uid, CancellationToken.None))!.ProductMasterId);
        }
        finally
        {
            await transaction.RollbackAsync();
            session.Transaction = null;
        }
    }

    /// <summary>基础 DTO、列表、详情和两类候选均保留自身型号，主型号投影独立；型号关键词可匹配自由文本。</summary>
    private static async Task AssertReadsAsync(PostgreSqlProductRepository repository, Product product,
        string? masterSku, string? linkSku, Guid categoryId, Guid? profileId)
    {
        var dto = await repository.GetDtoByIdAsync(product.Uid);
        Assert.IsNotNull(dto);
        Assert.AreEqual(product.ProductMasterId, dto.ProductMasterId);
        Assert.AreEqual(product.LinkSku ?? "", dto.LinkSku ?? "");
        Assert.AreEqual(product.ColorId ?? Guid.Empty, dto.ColorId);
        Assert.AreEqual(masterSku ?? "", dto.MasterSku ?? "");
        Assert.AreEqual(linkSku ?? "", dto.LinkSku ?? "");
        Assert.AreEqual(categoryId, dto.CategoryId);
        var bySku = await repository.GetDtoBySkuAsync(product.Sku);
        Assert.IsNotNull(bySku);
        Assert.AreEqual(linkSku ?? "", bySku.LinkSku ?? "");
        var page = await repository.GetPagedAsync(new ProductSearchRequest { Keyword = product.Sku, PageSize = 20 });
        var row = page.Items.Single(item => item.Uid == product.Uid);
        Assert.AreEqual(product.ProductMasterId, row.ProductMasterId);
        Assert.AreEqual(product.LinkSku ?? "", row.LinkSku ?? "");
        Assert.AreEqual(product.ColorId ?? Guid.Empty, row.ColorId);
        Assert.AreEqual(masterSku ?? "", row.MasterSku ?? "");
        Assert.AreEqual(linkSku ?? "", row.LinkSku ?? "");
        Assert.AreEqual(categoryId, row.CategoryId);
        var detail = await repository.GetDetailAsync(product.Uid);
        Assert.IsNotNull(detail);
        Assert.AreEqual(product.ProductMasterId, detail.ProductMasterId);
        Assert.AreEqual(product.LinkSku ?? "", detail.LinkSku ?? "");
        Assert.AreEqual(product.ProductMasterId, detail.Data.ProductMasterId);
        Assert.AreEqual(product.LinkSku ?? "", detail.Data.LinkSku ?? "");
        Assert.AreEqual(product.ColorId, detail.Data.ColorId);
        Assert.AreEqual(masterSku ?? "", detail.MasterSku ?? "");
        Assert.AreEqual(linkSku ?? "", detail.LinkSku ?? "");
        Assert.AreEqual(categoryId, detail.CategoryId);
        Assert.AreEqual(profileId, detail.ProductAttributeProfileId);
        Assert.AreEqual(product.UnitWeight, detail.Data.UnitWeight);
        Assert.AreEqual(product.UnitCBM, detail.Data.UnitCBM);
        Assert.AreEqual(product.BomNo, detail.BomNo);
        Assert.AreEqual(product.BomVersion, detail.BomVersion);
        var lookup = await repository.SearchLookupAsync(new ProductLookupRequest { Keyword = product.Sku, Limit = 20 });
        Assert.AreEqual(linkSku ?? "", lookup.Single(item => item.Uid == product.Uid).LinkSku ?? "");
        var sales = await repository.SearchSalesOrderProductsAsync(new SalesOrderProductSearchRequest
        { Keyword = product.Sku, PageSize = 20 });
        Assert.AreEqual(linkSku ?? "", sales.Items.Single(item => item.ProductId == product.Uid).LinkSku ?? "");
        if (!string.IsNullOrEmpty(linkSku))
        {
            var modelLookup = await repository.SearchLookupAsync(new ProductLookupRequest { Keyword = linkSku, Limit = 20 });
            Assert.AreEqual(linkSku, modelLookup.Single(item => item.Uid == product.Uid).LinkSku);
            var modelSales = await repository.SearchSalesOrderProductsAsync(new SalesOrderProductSearchRequest
            { Keyword = linkSku, PageSize = 20 });
            Assert.AreEqual(linkSku, modelSales.Items.Single(item => item.ProductId == product.Uid).LinkSku);
        }
    }

    /// <summary>仅为仓储审计提供固定测试用户，不构造登录或权限运行状态。</summary>
    private sealed class TestUser : ICurrentUser
    {
        /// <inheritdoc />
        public string UserName => "product-source-projection-tests";
        /// <inheritdoc />
        public Guid? UserId { get; } = Guid.NewGuid();
        /// <inheritdoc />
        public bool IsAuthenticated => true;
    }
}

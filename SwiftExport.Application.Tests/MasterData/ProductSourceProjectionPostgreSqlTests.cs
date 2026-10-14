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

/// <summary>物料独立来源的真实 PG 投影验收；临时结构及随机身份夹具均位于同一回滚事务。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductSourceProjectionPostgreSqlTests
{
    /// <summary>移除旧编码列后，跨主型号来源、空/失效来源及清空写入在全部物料读取入口保持一致。</summary>
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
                    DROP COLUMN IF EXISTS link_sku,
                    ADD COLUMN IF NOT EXISTS product_neutral_id uuid NULL;
                """, transaction: transaction);
            Assert.IsTrue(await session.Connection.ExecuteScalarAsync<bool>("""
                SELECT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                    AND table_name='product' AND column_name='product_neutral_id' AND data_type='uuid' AND is_nullable='YES')
                    AND NOT EXISTS(SELECT 1 FROM information_schema.columns WHERE table_schema='public'
                    AND table_name='product' AND column_name IN ('master_sku','link_sku'))
                """, transaction: transaction));

            var user = new TestUser();
            var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(user), user);
            var masters = new PostgreSqlDapperRepository<ProductMaster>(context);
            var neutrals = new PostgreSqlDapperRepository<ProductNeutral>(context);
            var products = new PostgreSqlProductRepository(context);
            var prefix = "SOURCE_" + Guid.NewGuid().ToString("N");
            var masterA = new ProductMaster
            {
                MasterSku = prefix + "_A", CategoryId = Guid.NewGuid(), DefaultPcsPerPackage = 1,
                ProductAttributeProfileId = Guid.NewGuid()
            };
            var masterB = new ProductMaster
            {
                MasterSku = prefix + "_B", CategoryId = Guid.NewGuid(), DefaultPcsPerPackage = 1,
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
                Sku = prefix + "_P", ProductMasterId = masterA.Uid, ProductNeutralId = neutralB.Uid,
                ColorId = Guid.NewGuid(), DescriptionEn = "物料独立描述", FeaturesZh = "物料独立配置",
                SupplyType = "采购", MaterialType = "成品", Unit = "PCS", UnitWeight = 2.75m, UnitCBM = 0.025m,
                BomNo = prefix + "_BOM", BomVersion = 1, Note = "来源变更保留其他资料"
            };
            Assert.IsTrue((await products.AddAsync(product)).Success);

            // 中性产品属于 B，不得让物料主型号、分类或属性模板从 A 被改投影为 B。
            await AssertReadsAsync(products, product, masterA.MasterSku, neutralB.ProductCode,
                masterA.CategoryId, masterA.ProductAttributeProfileId);
            foreach (var scenario in new[] { "master-only", "neutral-only", "empty", "missing" })
            {
                product.ProductMasterId = scenario == "master-only" ? masterA.Uid
                    : scenario == "missing" ? Guid.NewGuid() : null;
                product.ProductNeutralId = scenario == "neutral-only" ? neutralB.Uid
                    : scenario == "missing" ? Guid.NewGuid() : null;
                var oldVersion = product.Version;
                Assert.IsTrue((await products.UpdateAsync(product)).Success);
                Assert.AreEqual(oldVersion + 1, product.Version);
                await AssertReadsAsync(products, product,
                    scenario == "master-only" ? masterA.MasterSku : null,
                    scenario == "neutral-only" ? neutralB.ProductCode : null,
                    scenario == "master-only" ? masterA.CategoryId : Guid.Empty,
                    scenario == "master-only" ? masterA.ProductAttributeProfileId : null);
            }

            product.ProductMasterId = masterA.Uid;
            product.ProductNeutralId = neutralB.Uid;
            Assert.IsTrue((await products.UpdateAsync(product)).Success);
            Assert.IsTrue((await masters.DeleteAsync(masterA.Uid, masterA.Version)).Success);
            await AssertReadsAsync(products, product, null, neutralB.ProductCode, Guid.Empty, null);
            Assert.IsTrue((await neutrals.DeleteAsync(neutralB.Uid, neutralB.Version)).Success);
            await AssertReadsAsync(products, product, null, null, Guid.Empty, null);

            product.ProductMasterId = null;
            product.ProductNeutralId = null;
            Assert.IsTrue((await products.UpdateAsync(product)).Success);
            await AssertReadsAsync(products, product, null, null, Guid.Empty, null);
            var persisted = await products.GetByIdAsync(product.Uid, CancellationToken.None);
            Assert.IsNotNull(persisted);
            Assert.IsNull(persisted.ProductMasterId);
            Assert.IsNull(persisted.ProductNeutralId);
            Assert.AreEqual(product.DescriptionEn, persisted.DescriptionEn);
            Assert.AreEqual(product.FeaturesZh, persisted.FeaturesZh);
            Assert.AreEqual(product.ColorId, persisted.ColorId);
            Assert.AreEqual(product.UnitWeight, persisted.UnitWeight);
            Assert.AreEqual(product.UnitCBM, persisted.UnitCBM);
            Assert.AreEqual(product.BomNo, persisted.BomNo);
            Assert.AreEqual(product.BomVersion, persisted.BomVersion);
            Assert.AreEqual(product.Note, persisted.Note);
        }
        finally
        {
            await transaction.RollbackAsync();
            session.Transaction = null;
        }
    }

    /// <summary>同一物料在基础 DTO、列表、详情、销售候选中均读取来源投影；空名称允许 null 或空字符串。</summary>
    private static async Task AssertReadsAsync(PostgreSqlProductRepository repository, Product product,
        string? masterSku, string? linkSku, Guid categoryId, Guid? profileId)
    {
        var dto = await repository.GetDtoByIdAsync(product.Uid);
        Assert.IsNotNull(dto);
        Assert.AreEqual(product.ProductMasterId, dto.ProductMasterId);
        Assert.AreEqual(product.ProductNeutralId, dto.ProductNeutralId);
        Assert.AreEqual(masterSku ?? "", dto.MasterSku ?? "");
        Assert.AreEqual(linkSku ?? "", dto.LinkSku ?? "");
        Assert.AreEqual(categoryId, dto.CategoryId);
        var page = await repository.GetPagedAsync(new ProductSearchRequest { Keyword = product.Sku, PageSize = 20 });
        var row = page.Items.Single(item => item.Uid == product.Uid);
        Assert.AreEqual(product.ProductMasterId, row.ProductMasterId);
        Assert.AreEqual(product.ProductNeutralId, row.ProductNeutralId);
        Assert.AreEqual(masterSku ?? "", row.MasterSku ?? "");
        Assert.AreEqual(linkSku ?? "", row.LinkSku ?? "");
        Assert.AreEqual(categoryId, row.CategoryId);
        var detail = await repository.GetDetailAsync(product.Uid);
        Assert.IsNotNull(detail);
        Assert.AreEqual(product.ProductMasterId, detail.ProductMasterId);
        Assert.AreEqual(product.ProductNeutralId, detail.ProductNeutralId);
        Assert.AreEqual(product.ProductMasterId, detail.Data.ProductMasterId);
        Assert.AreEqual(product.ProductNeutralId, detail.Data.ProductNeutralId);
        Assert.AreEqual(masterSku ?? "", detail.MasterSku ?? "");
        Assert.AreEqual(linkSku ?? "", detail.LinkSku ?? "");
        Assert.AreEqual(categoryId, detail.CategoryId);
        Assert.AreEqual(profileId, detail.ProductAttributeProfileId);
        var sales = await repository.SearchSalesOrderProductsAsync(new SalesOrderProductSearchRequest
        { Keyword = product.Sku, PageSize = 20 });
        Assert.AreEqual(linkSku ?? "", sales.Items.Single(item => item.ProductId == product.Uid).LinkSku ?? "");
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

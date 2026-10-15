using System.Data;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.Products.Catalog;
using SwiftExport.Infrastructure.Repositories.Shipping;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>获准专用库的三入口外箱取数矩阵；不初始化或清理数据库，全部随机事实由父事务回滚。</summary>
public sealed partial class ProductPackageRelationshipTests
{
    /// <summary>有效外箱胜过其他包装装率，缺失或软删不回退；非标和断链 SO/CR 来源保持原有语义。</summary>
    [TestMethod]
    [TestCategory("PostgreSqlPackageIntegration")]
    [DataRow("active", "SO", false)]
    [DataRow("missing", "SO", false)]
    [DataRow("deleted", "CR", false)]
    [DataRow("active", "SO", true)]
    [DataRow("active", "CR", false)]
    [DataRow("active", "CR", true)]
    public async Task CartonQueries_ShouldAgreeAndKeepSourceBoundaries(string cartonState, string sourceType, bool nonStandard)
    {
        using var session = NewTestSession();
        using var transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        session.Transaction = transaction;
        try
        {
            var context = new RepositoryBaseContext(session, NullLoggerFactory.Instance, new AuditStampService(new TestUser()), new TestUser());
            var products = new PostgreSqlDapperRepository<Product>(context);
            var owner = new Product { Sku = "CARTON_" + Guid.NewGuid().ToString("N"), SupplyType = "采购" };
            var other = new Product { Sku = "OTHER_" + Guid.NewGuid().ToString("N"), SupplyType = "采购" };
            Assert.IsTrue((await products.AddAsync(owner)).Success);
            Assert.IsTrue((await products.AddAsync(other)).Success);
            var packages = new PostgreSqlDapperRepository<ProductPackage>(context);
            // 非外箱以及中文显示名都不能充当稳定 Carton 代码，其他物料外箱也不能串入。
            foreach (var type in new[] { ProductPackageTypeCodes.InnerBox, ProductPackageTypeCodes.ShippingBox,
                         ProductPackageTypeCodes.Pallet, "CustomPackage", "出货外箱" })
                Assert.IsTrue((await packages.AddAsync(new ProductPackage { OwnerProductId = owner.Uid,
                    PackageType = type, PcsPerPackage = 999, Nw_KG = 999, Gw_KG = 999, Cbm = 999 })).Success);
            Assert.IsTrue((await packages.AddAsync(new ProductPackage { OwnerProductId = other.Uid, PcsPerPackage = 555 })).Success);
            if (cartonState != "missing")
                Assert.IsTrue((await packages.AddAsync(new ProductPackage { OwnerProductId = owner.Uid,
                    PackageType = ProductPackageTypeCodes.Carton, PcsPerPackage = 12, Nw_KG = 20, Gw_KG = 22,
                    Cbm = 0.4m, IsDeleted = cartonState == "deleted" })).Success);

            var catalog = new PostgreSqlProductRepository(context);
            var page = (await catalog.GetPagedAsync(new() { Keyword = owner.Sku })).Items.Single();
            var detail = await catalog.GetDetailAsync(owner.Uid);
            Assert.IsNotNull(detail);
            var hasCarton = cartonState == "active";
            Assert.AreEqual(hasCarton ? 12m : (decimal?)null, page.PcsOfCarton);
            Assert.AreEqual(page.PcsOfCarton, detail.PcsOfCarton);
            Assert.AreEqual(hasCarton ? 20m : (decimal?)null, page.NwOfCarton);
            Assert.AreEqual(page.NwOfCarton, detail.NwOfCarton);
            Assert.AreEqual(hasCarton ? 22m : (decimal?)null, page.GwOfCarton);
            Assert.AreEqual(page.GwOfCarton, detail.GwOfCarton);
            Assert.AreEqual(hasCarton ? 0.4m : (decimal?)null, page.CbmOfCarton);
            Assert.AreEqual(page.CbmOfCarton, detail.CbmOfCarton);

            var source = new GrossDemand { SourceType = sourceType, SourceUid = Guid.NewGuid(), SourceLineId = Guid.NewGuid(),
                ProductId = nonStandard ? null : owner.Uid, IsNonStandard = nonStandard, DemandQty = 24, UnitSnapshot = "PCS" };
            Assert.IsTrue((await new PostgreSqlDapperRepository<GrossDemand>(context).AddAsync(source)).Success);
            var enrichment = (await new PostgreSqlPackingListRepository(context).GetSourceEnrichmentsAsync([source.Uid])).Single();
            Assert.AreEqual(source.Uid, enrichment.GrossDemandId);
            Assert.AreEqual(hasCarton && !nonStandard ? 12m : (decimal?)null, enrichment.PcsPerPackage);
            Assert.AreEqual(hasCarton && !nonStandard ? 20m : (decimal?)null, enrichment.NwPerPackage);
            Assert.AreEqual(hasCarton && !nonStandard ? 22m : (decimal?)null, enrichment.GwPerPackage);
            Assert.AreEqual(hasCarton && !nonStandard ? 0.4m : (decimal?)null, enrichment.CbmPerPackage);
            // 断链来源不丢弃毛需求或补造单号，包装读取资格也不因来源类型改变。
            Assert.IsNull(enrichment.CustomerPoNo);
            Assert.IsNull(enrichment.InvoiceNo);
            source.IsDeleted = true;
            Assert.IsTrue((await new PostgreSqlDapperRepository<GrossDemand>(context).UpdateAsync(source)).Success);
            Assert.HasCount(0, await new PostgreSqlPackingListRepository(context).GetSourceEnrichmentsAsync([source.Uid]));
        }
        finally
        {
            try { transaction.Rollback(); }
            finally { session.Transaction = null; }
        }
    }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Sources;

namespace SwiftExport.Application.Tests.DemandPlanning;

/// <summary>在随机隔离 LocalDB 验证单位快照持久化及真实列表、追踪和采购候选，不访问业务库。</summary>
[TestClass, DoNotParallelize]
public sealed class DemandUnitSqlTests
{
    /// <summary>写入后修改当前产品单位，全部消费者仍保留物料单位；未知根不借来源或当前产品补值。</summary>
    [TestMethod]
    public async Task StoredUnitSurvivesProductChangesAndUnknownRemainsUnknown()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_SQL_TESTS") != "1")
            Assert.Inconclusive("设置 SWIFTEXPORT_RUN_SQL_TESTS=1 后运行隔离 LocalDB 测试。");
        await using var db = new IndependentProcurementSqlFixture();
        await db.InitializeAsync();
        var grossId = Guid.NewGuid();
        await db.ExecuteAsync("""
            INSERT dbo.GrossDemand(Uid,LifecycleStatus,SourceType,SourceUid,SourceNo,SourceLineId,
                CustomerId,DemandBasisFingerprint,IsNonStandard,ProductId,SkuSnapshot,UnitSnapshot,DemandQty)
            VALUES(@grossId,0,N'MD',@grossId,N'MD-UNIT',@grossId,@customer,N'UNIT-BASIS',0,@product,N'ROOT',N'SET',10);
            """, new { grossId, customer = db.CustomerId, product = db.ProductId });
        var rootId = Guid.NewGuid();
        var rows = new[]
        {
            Row(rootId, 1, db.ProductId, "SET"),
            Row(Guid.NewGuid(), 2, db.ComponentId, "KG"),
            Row(Guid.NewGuid(), 3, null, "BOX"),
            Row(Guid.NewGuid(), 4, db.ProductId, null)
        };
        rows[1].ParentNetDemandId = rootId;
        await db.InScopeAsync(async p =>
        {
            var writes = await p.GetRequiredService<IMaterialDemandDocumentRepository>().AddDecompositionAsync(rows);
            Assert.IsTrue(writes.All(x => x.Success));
            var stored = await p.GetRequiredService<IMaterialDemandDocumentRepository>().GetActiveByGrossDemandForUpdateAsync(grossId);
            CollectionAssert.AreEqual(new string?[] { "SET", "KG", "BOX", null }, stored.Select(x => x.UnitSnapshot).ToArray());
            return true;
        });
        await db.ExecuteAsync("UPDATE dbo.Product SET Unit=N'CURRENT' WHERE Uid IN (@product,@component)",
            new { product = db.ProductId, component = db.ComponentId });
        await db.InScopeAsync(async p =>
        {
            var query = p.GetRequiredService<IMaterialDemandQueryRepository>();
            var list = await query.SearchManagerAsync(new MaterialDemandManagerSearchRequest { PageSize = 20 });
            var candidates = await p.GetRequiredService<IMaterialDemandSourceRepository>()
                .GetPurchaseCandidatesAsync(new PurchaseDraftCandidateQuery { SourceType = "MD", SourceUid = grossId });
            foreach (var row in rows)
            {
                Assert.AreEqual(row.UnitSnapshot, list.Items.Single(x => x.Uid == row.Uid).Unit);
                Assert.AreEqual(row.UnitSnapshot, candidates.Single(x => x.Uid == row.Uid).Unit);
                var trace = await query.GetTraceAsync(row.Uid);
                if (row.UnitSnapshot is null) Assert.IsTrue(string.IsNullOrWhiteSpace(trace!.Unit));
                else Assert.AreEqual(row.UnitSnapshot, trace!.Unit);
            }
            StringAssert.Contains(list.Items.Single(x => x.Uid == rows[3].Uid).ReviewReason, "单位");
            return true;
        });

        // 各行共享来源事实，仅物料身份与单位快照不同，未知单位不以来源 SET 或当前 CURRENT 兜底。
        NetDemand Row(Guid uid, int line, Guid? product, string? unit) => new()
        {
            Uid = uid, GrossDemandId = grossId, DecompositionRevision = 1, LineNumber = line,
            SourceDemandVersionSnapshot = 1, SourceDemandBasisFingerprintSnapshot = "UNIT-BASIS",
            RootNetDemandId = rootId, ProductId = product, UnitSnapshot = unit,
            SkuSnapshot = "ROW-" + line, SupplyTypeSnapshot = SupplyTypeEnums.采购, GrossQty = 10, NetQty = 10
        };
    }
}

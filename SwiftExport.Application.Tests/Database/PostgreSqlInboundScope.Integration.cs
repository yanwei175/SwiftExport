using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用随机身份、单事务回滚的隔离夹具，验证真实 PG 整头异常范围和候选分页。</summary>
public sealed partial class PostgreSqlInboundQueryMigrationTests
{
    /// <summary>真实仓储只允许未删除且启用的原仓库，与反向预览和库存内核保持相同边界。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundScopeIntegration")]
    public Task RealPg_InboundReverseWarehouseRequiresActiveSite()=>Rollback(async f=>
    {
        var inactive=new WarehouseSite{Code=f.Prefix+"inactive",Name="停用仓",SupplierId=Guid.NewGuid(),IsActive=false};
        var active=new WarehouseSite{Code=f.Prefix+"active",Name="启用仓",SupplierId=Guid.NewGuid(),IsActive=true};
        var deleted=new WarehouseSite{Code=f.Prefix+"deleted",Name="删除仓",SupplierId=Guid.NewGuid(),IsActive=true,IsDeleted=true};
        await f.Add(inactive);await f.Add(active);await f.Add(deleted);
        var user=new WorkspaceUser();var reads=new PostgreSqlInboundExecutionReadRepository(new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user));
        Assert.IsFalse(await reads.ReverseWarehouseExistsAsync(inactive.Uid,default));
        Assert.IsTrue(await reads.ReverseWarehouseExistsAsync(active.Uid,default));
        Assert.IsFalse(await reads.ReverseWarehouseExistsAsync(deleted.Uid,default));
        Assert.IsFalse(await reads.ReverseWarehouseExistsAsync(Guid.NewGuid(),default));
        Assert.AreSame(f.Transaction,f.Session.Transaction);
    });

    /// <summary>人工和自动异常头只污染关联采购行；未知来源、软删关联及不完整反向均不能按零收货处理。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundScopeIntegration")]
    [DataRow("unknown",false)] [DataRow("unknown",true)]
    [DataRow("deletedSibling",false)] [DataRow("deletedSibling",true)]
    [DataRow("missingReversal",false)] [DataRow("missingReversal",true)]
    [DataRow("legacy11",false)] [DataRow("legacy12",true)]
    public Task RealPg_ScopedSourcesKeepUnrelatedRowsAndCandidatePaging(string anomaly,bool automatic)=>Rollback(async f=>
    {
        f.Purchase.ConfirmationStatus=ConfirmationStatusEnum.已确认;f.PurchaseLine.MaterialType="成品";
        await f.Seed();
        var badSource=ScopePurchaseLine(f,2);var healthySource=ScopePurchaseLine(f,3);
        await f.Add(badSource);await f.Add(healthySource);
        var bad=f.Head("bad-scope");bad.PostingStatus=InboundPostingStatusEnum.Posted;bad.PostedDate=f.Today;
        if(automatic){bad.CreationMode=InboundCreationModeEnum.Automatic;bad.TriggerPackingListId=Guid.NewGuid();}
        if(anomaly=="missingReversal"){bad.PostingStatus=InboundPostingStatusEnum.Reversed;bad.ReversedDate=f.Today;bad.ReverseReason="缺反向事实";}
        if(anomaly is "legacy11" or "legacy12")bad.DocumentType=(InboundDocumentTypeEnum)(anomaly=="legacy11"?11:12);
        await f.Add(bad);var badLine=f.Line(bad,1,4);badLine.SourceLineId=badSource.Uid;await f.Add(badLine);
        if(anomaly=="unknown")
        {
            var unknown=f.Line(bad,2,1);unknown.SourceLineId=Guid.NewGuid();await f.Add(unknown);
        }
        Guid? siblingId=null;
        if(anomaly=="deletedSibling")
        {
            var siblingSource=ScopePurchaseLine(f,4);await f.Add(siblingSource);
            siblingId=siblingSource.Uid;
            var deleted=f.Line(bad,2,1);deleted.SourceLineId=siblingSource.Uid;deleted.IsDeleted=true;await f.Add(deleted);
        }
        // 完全孤立的坏头没有关联采购行，不能变成全库门禁。
        var isolated=f.Head("isolated-bad");isolated.PostingStatus=InboundPostingStatusEnum.Posted;isolated.PostedDate=f.Today;
        await f.Add(isolated);var orphan=f.Line(isolated,1,1);orphan.SourceLineId=Guid.NewGuid();orphan.OriginalLineId=Guid.NewGuid();await f.Add(orphan);
        var healthy=f.Head("healthy-draft");await f.Add(healthy);await f.Add(f.Line(healthy,1,2));
        var related=f.Head("related-draft");await f.Add(related);var relatedLine=f.Line(related,1,2);relatedLine.SourceLineId=badSource.Uid;await f.Add(relatedLine);
        var user=new WorkspaceUser();var sources=new PostgreSqlInboundSourceRepository(new RepositoryBaseContext(f.Session,NullLoggerFactory.Instance,new AuditStampService(user),user));
        Assert.IsFalse(await sources.HasUnprovenReceiptAsync([f.PurchaseLine.Uid,healthySource.Uid],default));
        Assert.IsTrue(await sources.HasUnprovenReceiptAsync([badSource.Uid],default));
        Assert.AreEqual(100m,(await f.Repository.GetDetailAsync(healthy.Uid,default))!.Lines.Single().RemainingReceivableQty);
        Assert.IsNull(await f.Repository.GetDetailAsync(Guid.NewGuid(),default));
        Assert.AreEqual(f.PurchaseLine.Uid,(await sources.GetSourcesAsync([f.PurchaseLine.Uid],default)).Single().SourceLineId);
        Assert.AreEqual(0m,(await sources.ReadPostingSourcesAsync([healthySource.Uid],default)).Single().PostedNetQty);
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Repository.GetDetailAsync(related.Uid,default));
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>sources.GetSourcesAsync([badSource.Uid],default));
        await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>sources.ReadPostingSourcesAsync([badSource.Uid],default));
        if(siblingId.HasValue)
            await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>sources.GetSourcesAsync([siblingId.Value],default));
        var selected=new List<Guid>();
        for(var page=1;page<=3;page++)
        {
            var result=await sources.SearchSourcesAsync(new InboundSourceSearchRequest{Keyword=f.Prefix,PurchaseCategory=InboundPurchaseCategoryEnum.Normal,PageIndex=page,PageSize=1},default);
            Assert.AreEqual(2,result.TotalCount);Assert.HasCount(page<=2?1:0,result.Items);
            selected.AddRange(result.Items.Select(x=>x.SourceLineId));
        }
        CollectionAssert.AreEquivalent(new[]{f.PurchaseLine.Uid,healthySource.Uid},selected);
        Assert.AreEqual(1,await f.Session.Connection.ExecuteScalarAsync<int>("SELECT 1",transaction:f.Transaction));
    });

    /// <summary>同一已确认采购头下的合法来源快照仅用于本例，不新增通用测试数据初始化器。</summary>
    private static PurchaseOrderLine ScopePurchaseLine(InboundFixture f,int number)=>new()
    {
        PurchaseOrderId=f.Purchase.Uid,LineNumber=number,ProductId=f.Product.Uid,Quantity=100,
        Sku=f.Prefix+"source"+number,UnitSnapshot="PCS",MaterialType="成品"
    };
}

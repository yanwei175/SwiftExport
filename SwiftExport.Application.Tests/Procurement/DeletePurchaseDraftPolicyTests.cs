using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Reversal;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>整单删除仅限从未确认且无下游历史的活动草稿；释放范围及历史证据不能被当前状态掩盖。</summary>
[TestClass]
public sealed class DeletePurchaseDraftPolicyTests
{
    /// <summary>无Link的独立草稿也不能把异常数量或未知未关联量当作安全删除资格。</summary>
    [TestMethod,DataRow("zero"),DataRow("unknown"),DataRow("mismatch")]
    public void InvalidIndependentQuantityBlocksDeletion(string fault)
    {
        var facts=Facts();
        if(fault=="zero")facts.Lines[0].Quantity=0;
        else if(fault=="unknown")facts.Lines[0].UnlinkedQty=null;
        else facts.Lines[0].UnlinkedQty=2;
        Assert.IsFalse(Build(facts).CanExecute);
    }
    /// <summary>当前草稿即使清空确认标识，持久确认或任何下游历史仍必须拒绝删除。</summary>
    [TestMethod,DataRow("confirmed"),DataRow("history"),DataRow("downstream"),DataRow("inbound"),DataRow("ledger"),DataRow("feedback"),DataRow("closed")]
    public void PriorHistoryBlocksDeletion(string fault)
    {
        var facts=Facts();
        switch(fault)
        {
            case "confirmed":facts.Head!.ConfirmationStatus=ConfirmationStatusEnum.已确认;break;
            case "history":facts.HasConfirmationHistory=true;break;
            case "downstream":facts.HasDownstreamHistory=true;break;
            case "inbound":facts.Inbounds=[new InboundDocument{IsDeleted=true}];break;
            case "ledger":facts.Ledgers=[new InventoryLedgerEntry()];break;
            case "feedback":facts.Reports=[new SupplierProgressReport{PurchaseOrderLineId=facts.Lines[0].Uid,IsDeleted=true}];break;
            case "closed":facts.Head!.LifecycleStatus=PurchaseOrderLifecycleStatusEnum.Closed;break;
        }
        Assert.IsFalse(Build(facts).CanExecute);
    }

    /// <summary>全量行版本与占用守恒正确时可删除；单行删除仍不允许偷偷全删。</summary>
    [TestMethod]
    public void WholeDraftRequiresAllCurrentVersions()
    {
        var facts=Facts();var request=Request(facts);
        Assert.IsTrue(Build(facts).CanExecute);
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteLines,request).CanExecute);
        facts.Lines.Add(new(){Uid=Guid.NewGuid(),Version=1,Quantity=2,UnlinkedQty=2});
        Assert.IsFalse(ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteDraft,request).CanExecute);
        request=Request(facts);request.Lines[0].Version++;
        Assert.Throws<SwiftExport.Application.Services.Procurement.PurchaseOrders.Support.ProcurementOperationException>(()=>ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteDraft,request));
    }

    /// <summary>草稿Link占用可按本单完整释放，取消确认保留相同贡献；异常零净贡献不能冒充独立采购。</summary>
    [TestMethod]
    public void DraftReferencesAreReleasedOnlyByDeleteOrVoid()
    {
        var facts=Facts();var source=new GrossDemand{Uid=Guid.NewGuid(),SourceNo="MD-TEST"};
        var demand=new NetDemand{Uid=Guid.NewGuid(),GrossDemandId=source.Uid};
        facts.Sources=[source];facts.Demands=[demand];facts.Head!.OriginKind=1;facts.Lines[0].UnlinkedQty=0;
        facts.Links=[new DemandExecutionLink{Uid=Guid.NewGuid(),NetDemandId=demand.Uid,TargetHeadId=facts.Head.Uid,
            TargetLineId=facts.Lines[0].Uid,TargetType="PO",AllocatedQty=3}];
        var deletion=Build(facts);Assert.IsTrue(deletion.CanExecute,string.Join(";",deletion.Blockers));Assert.AreEqual(3m,deletion.Contributions.Single().Quantity);
        facts.Head.ConfirmationStatus=ConfirmationStatusEnum.已确认;
        var cancel=ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.CancelConfirmation,new(){ExpectedVersion=facts.Head.Version});
        Assert.IsTrue(cancel.CanExecute);Assert.IsEmpty(cancel.Contributions);Assert.AreEqual(DemandExecutionLinkStatusEnum.Active,facts.Links[0].LinkStatus);
        facts.Head.ConfirmationStatus=ConfirmationStatusEnum.待确认;facts.Links[0].ReleasedQty=3;
        Assert.IsFalse(Build(facts).CanExecute);
    }

    /// <summary>独立采购的有效最小事实，不借BOM或来源身份构造删除资格。</summary>
    private static ProcurementReversalFacts Facts()=>new(){Head=new(){Uid=Guid.NewGuid(),Version=1,OriginKind=2},
        Lines=[new PurchaseOrderLine{Uid=Guid.NewGuid(),Version=1,Quantity=3,UnlinkedQty=3}]};
    /// <summary>提交当前全部行版本，不能用只有头版本覆盖部分行变化。</summary>
    private static ProcurementReversalPreviewRequest Request(ProcurementReversalFacts facts)=>new(){ExpectedVersion=facts.Head!.Version,
        Lines=facts.Lines.Where(line=>!line.IsDeleted).Select(line=>new ProcurementLineVersionDto{PurchaseOrderLineId=line.Uid,Version=line.Version}).ToList()};
    /// <summary>只调用真实整单删除策略，不在测试中重算资格。</summary>
    private static ProcurementReversalPreviewDto Build(ProcurementReversalFacts facts)=>ProcurementReversalPolicy.Build(facts,ProcurementReversalOperation.DeleteDraft,Request(facts));
}

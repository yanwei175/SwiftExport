using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Association;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Confirmation;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>确认和补关联共用已有需求引用规则；验证完整坏Link不会被过滤成独立采购，当前请求版本仍防并发。</summary>
[TestClass]
public sealed class PurchaseOrderReferencePolicyTests
{
    /// <summary>两个真实策略入口拒绝相同的来源、目标、单位、数量及坏活动引用；不模拟数据库约束。</summary>
    [TestMethod,DataRow("fingerprint"),DataRow("customer"),DataRow("product"),DataRow("unit"),DataRow("closed"),DataRow("deleted-source"),
     DataRow("over-demand"),DataRow("unlinked"),DataRow("target-head"),DataRow("target-line"),DataRow("active-zero"),DataRow("active-negative"),
     DataRow("released-excess"),DataRow("active-deleted"),DataRow("inactive-remaining")]
    public void ExistingReferences_RejectSameInvalidFactsAtBothEntrypoints(string fault)
    {
        var f=new Facts();ProcurementConfirmationBuilder.RequireSourceReferences(f.Value);_=ProcurementDemandAssociationPolicy.Build(f.Value,f.Targets,1,[]);
        switch(fault)
        {
            case "fingerprint":f.Material.SourceDemandBasisFingerprintSnapshot=null;break;
            case "customer":f.Source.CustomerId=Guid.NewGuid();break;
            case "product":f.Line.ProductId=Guid.NewGuid();break;
            case "unit":f.Material.UnitSnapshot="BOX";break;
            case "closed":f.Material.LifecycleStatus=NetDemandLifecycleStatusEnum.RolledBack;break;
            case "deleted-source":f.Source.IsDeleted=true;break;
            case "over-demand":f.Material.NetQty=5;break;
            case "unlinked":f.Line.UnlinkedQty=3;break;
            case "target-head":f.Link.TargetHeadId=Guid.NewGuid();break;
            case "target-line":f.Link.TargetLineId=Guid.NewGuid();break;
            case "active-zero":f.Link.ReleasedQty=f.Link.AllocatedQty;f.Line.UnlinkedQty=f.Line.Quantity;break;
            case "active-negative":f.Link.AllocatedQty=0;f.Line.UnlinkedQty=f.Line.Quantity;break;
            case "released-excess":f.Link.ReleasedQty=9;f.Line.UnlinkedQty=f.Line.Quantity;break;
            case "active-deleted":f.Link.IsDeleted=true;f.Line.UnlinkedQty=f.Line.Quantity;break;
            case "inactive-remaining":f.Link.LinkStatus=DemandExecutionLinkStatusEnum.Cancelled;f.Line.UnlinkedQty=f.Line.Quantity;break;
        }
        Assert.Throws<Exception>(()=>ProcurementConfirmationBuilder.RequireSourceReferences(f.Value));
        Assert.Throws<Exception>(()=>ProcurementDemandAssociationPolicy.Build(f.Value,f.Targets,1,[]));
    }

    /// <summary>同指纹技术快照落后仍相容；补关联必须提交当前来源版本，合法历史完全释放贡献不占容量。</summary>
    [TestMethod]
    public void TechnicalVersionAndReleasedHistory_PreserveTheirSeparateMeanings()
    {
        var f=new Facts();f.Source.Version=9;f.Source.SourceVersion=7;f.Material.SourceDemandVersionSnapshot=1;f.Material.SourceVersionSnapshot=1;
        var item=new ProcurementDemandAssociationItem{CommandItemId=Guid.NewGuid(),PurchaseOrderLineId=f.Line.Uid,NetDemandId=f.Material.Uid,
            ExpectedLineVersion=1,ExpectedDemandVersion=1,ExpectedSourceDemandVersion=9,Quantity=1};
        ProcurementConfirmationBuilder.RequireSourceReferences(f.Value);Assert.HasCount(1,ProcurementDemandAssociationPolicy.Build(f.Value,f.Targets,1,[item]).Items);
        item.ExpectedSourceDemandVersion=1;Assert.Throws<Exception>(()=>ProcurementDemandAssociationPolicy.Build(f.Value,f.Targets,1,[item]));
        f.Link.LinkStatus=DemandExecutionLinkStatusEnum.Cancelled;f.Link.ReleasedQty=f.Link.AllocatedQty;f.Line.UnlinkedQty=f.Line.Quantity;
        ProcurementConfirmationBuilder.RequireSourceReferences(f.Value);
    }

    /// <summary>确认在既有来源锁下读取目标，和补关联同样核实客供件同树父件及实际送达供应商。</summary>
    [TestMethod,DataRow("supplier"),DataRow("parent-root"),DataRow("parent-product")]
    public async Task CustomerSuppliedRelations_AreCheckedByConfirmationAndAssociation(string fault)
    {
        var f=new Facts();var parentSupplier=Guid.NewGuid();var parent=new NetDemand{Uid=Guid.NewGuid(),GrossDemandId=f.Source.Uid,ProductId=Guid.NewGuid(),
            DecompositionRevision=1,NetQty=4,UnitSnapshot="PCS",MaterialTypeSnapshot="零件",SupplyTypeSnapshot="采购",SourceDemandBasisFingerprintSnapshot="basis"};
        parent.RootNetDemandId=parent.Uid;f.Value.Demands.Add(parent);
        f.Material.MaterialTypeSnapshot=f.Line.MaterialType="客供件";f.Material.ParentNetDemandId=parent.Uid;f.Material.RootNetDemandId=parent.Uid;
        f.Material.DeliveryTargetNetDemandId=parent.Uid;f.Material.DeliveryMode=DemandDeliveryModeEnum.ParentExecutionSupplier;f.Line.DeliveryTargetSupplierId=parentSupplier;
        f.Targets[0].DeliveryTargetSupplierId=parentSupplier;
        var parentLine=Guid.NewGuid();var parentHead=Guid.NewGuid();f.Value.Links.Add(new(){Uid=Guid.NewGuid(),NetDemandId=parent.Uid,TargetType="PO",TargetHeadId=parentHead,TargetLineId=parentLine,AllocatedQty=4});
        f.Targets.Add(new(){HeadId=parentHead,LineId=parentLine,SupplierId=parentSupplier,ProductId=parent.ProductId});
        var references=DispatchProxy.Create<IProcurementReferenceReadRepository,Probe>();((Probe)(object)references).Read=(method,args)=>method.Name switch
        {
            "GetCustomerAsync"=>Task.FromResult<Customer?>(new(){Uid=f.Head.CustomerId!.Value,CustomerCode="C",DocRelateNo="C",IsActive=true}),
            "GetSupplierAsync"=>Task.FromResult<Supplier?>(new(){Uid=(Guid)args[0]!,SupplierCode="S",SupplierName="供应商",DocRelateNo="S",IsActive=true}),
            "GetProductAsync"=>Task.FromResult<Product?>(new(){Uid=f.Line.ProductId!.Value,Sku="P",IsActive=true,Unit="PCS",MaterialType="客供件",SupplyType="采购"}),
            "GetUnitAsync"=>Task.FromResult<string?>("PCS"),_=>throw new AssertFailedException(method.Name)
        };
        var targetReads=DispatchProxy.Create<IProcurementDemandAssociationRepository,Probe>();((Probe)(object)targetReads).Read=(method,_)=>method.Name=="ReadTargetsAsync"
            ?Task.FromResult<IReadOnlyList<ProcurementAssociationTarget>>(f.Targets):throw new AssertFailedException(method.Name);
        var builder=new ProcurementConfirmationBuilder(references,targetReads);
        _=await builder.BuildAsync(f.Value,CancellationToken.None);_=ProcurementDemandAssociationPolicy.Build(f.Value,f.Targets,1,[]);
        if(fault=="supplier")f.Targets[0].DeliveryTargetSupplierId=f.Line.DeliveryTargetSupplierId=Guid.NewGuid();
        else if(fault=="parent-root")f.Material.RootNetDemandId=Guid.NewGuid();else f.Targets[1].ProductId=Guid.NewGuid();
        await Assert.ThrowsAsync<Exception>(()=>builder.BuildAsync(f.Value,CancellationToken.None));
        Assert.Throws<Exception>(()=>ProcurementDemandAssociationPolicy.Build(f.Value,f.Targets,1,[]));
    }

    /// <summary>只提供端口返回值，使用真实策略判断，不复制任何规则。</summary>
    public class Probe:DispatchProxy
    {
        internal Func<MethodInfo,object?[],object>? Read;
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>Read!(method!,args!);
    }

    /// <summary>合法的部分关联采购事实，供单一故障矩阵独立修改。</summary>
    private sealed class Facts
    {
        internal readonly PurchaseOrderHead Head=new(){Uid=Guid.NewGuid(),Version=1,OriginKind=2,CustomerId=Guid.NewGuid(),SupplierId=Guid.NewGuid()};
        internal readonly PurchaseOrderLine Line;internal readonly GrossDemand Source;internal readonly NetDemand Material;internal readonly DemandExecutionLink Link;
        internal readonly ProcurementReversalFacts Value;internal readonly List<ProcurementAssociationTarget> Targets;
        internal Facts()
        {
            Line=new(){Uid=Guid.NewGuid(),Version=1,PurchaseOrderId=Head.Uid,ProductId=Guid.NewGuid(),Quantity=10,UnlinkedQty=4,UnitSnapshot="PCS",MaterialType="成品",SupplyType="采购",UnitPrice=0,TaxPrice=0,UnitAmount=0,TaxAmount=0};
            Source=new(){Uid=Guid.NewGuid(),Version=1,CustomerId=Head.CustomerId,SourceType="SO",SourceUid=Guid.NewGuid(),SourceLineId=Guid.NewGuid(),SourceNo="SO-1",SourceVersion=1,DemandBasisFingerprint="basis"};
            Material=new(){Uid=Guid.NewGuid(),Version=1,GrossDemandId=Source.Uid,ProductId=Line.ProductId,NetQty=10,UnitSnapshot="PCS",MaterialTypeSnapshot="成品",SupplyTypeSnapshot="采购",DecompositionRevision=1,SourceDemandBasisFingerprintSnapshot="basis"};
            Link=new(){Uid=Guid.NewGuid(),NetDemandId=Material.Uid,TargetType="PO",TargetHeadId=Head.Uid,TargetLineId=Line.Uid,AllocatedQty=6};
            Value=new(){Head=Head,Lines=[Line],Sources=[Source],Demands=[Material],Links=[Link]};
            Targets=[new(){HeadId=Head.Uid,LineId=Line.Uid,ProductId=Line.ProductId,SupplierId=Head.SupplierId}];
        }
    }
}

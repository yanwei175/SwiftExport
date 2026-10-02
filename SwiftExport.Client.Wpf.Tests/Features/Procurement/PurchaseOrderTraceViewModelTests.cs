using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.SysEnums;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证采购创建类别与实际关联历史分别展示，刷新不会继续声称独立采购没有需求关系。</summary>
[TestClass]
public sealed class PurchaseOrderTraceViewModelTests
{
    /// <summary>同一独立采购从无关联到部分关联，再到已释放历史，均保持创建类别并刷新文案通知。</summary>
    [TestMethod]
    public void IndependentOriginDisplaysActualAssociationHistory()
    {
        // 仅验证派生展示属性，不执行任何 API、权限或导航调用。
        using var viewModel = new PurchaseOrderTraceViewModel(null!, null!, null!, null!);
        var notifications = new List<string?>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName);
        viewModel.Detail = new PurchaseOrderTraceDto { OriginKind = 2 };
        Assert.AreEqual("独立采购，无需求关联。", viewModel.OriginText);

        viewModel.Detail = new PurchaseOrderTraceDto { OriginKind = 2,
            Lines = [new() { UnlinkedQty = 4, Links = [new() { AllocatedQty = 6, EffectiveQty = 6 }] }] };
        Assert.AreEqual("独立采购；各行需求关联及历史以 Link 为准。", viewModel.OriginText);
        Assert.AreEqual(2, viewModel.Detail.OriginKind);

        viewModel.Detail = new PurchaseOrderTraceDto { OriginKind = 2,
            Lines = [new() { Links = [new() { LinkStatus = DemandExecutionLinkStatusEnum.Cancelled,
                AllocatedQty = 6, ReleasedQty = 6, EffectiveQty = 0 }] }] };
        Assert.AreEqual("独立采购；各行需求关联及历史以 Link 为准。", viewModel.OriginText);
        Assert.AreEqual(3, notifications.Count(x => x == nameof(viewModel.OriginText)));
    }

    /// <summary>需求驱动与未知创建类别不由是否存在 Link 推断为独立采购。</summary>
    [TestMethod]
    public void DemandAndUnknownOriginsKeepTheirIdentity()
    {
        using var viewModel = new PurchaseOrderTraceViewModel(null!, null!, null!, null!);
        viewModel.Detail = new PurchaseOrderTraceDto { OriginKind = 1 };
        Assert.AreEqual("需求驱动采购；各行来源以 Link 为准。", viewModel.OriginText);
        viewModel.Detail = new PurchaseOrderTraceDto { OriginKind = 0 };
        Assert.AreEqual("来源类别未确认，请核实历史采购单。", viewModel.OriginText);
    }
}

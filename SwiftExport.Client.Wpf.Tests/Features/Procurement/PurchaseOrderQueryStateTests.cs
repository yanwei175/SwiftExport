using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>多选精确条件冻结，翻页刷新不读取输入，查询失败不提交目标页。</summary>
    [TestMethod]
    public async Task AppliedQuery_ExactMultiSelectAndFailedPagingPreserveSnapshot()
    {
        var fixture = CreateFixture(Order("PO-FILTER")); var vm = fixture.ViewModel; PurchaseOrderManagerSearchRequest? last = null;
        fixture.Manager.SearchHandler = request => { last = request; return Task.FromResult(Page([Order("PO-RESULT")], request.PageIndex, request.PageSize)); };
        vm.Keyword = "old"; vm.SelectedCustomers.Add(new() { CustomerCode = "C1" }); var supplier = Guid.NewGuid(); vm.SelectedSuppliers.Add(new() { Uid = supplier });
        await vm.InitializeAsync(); vm.Keyword = "draft"; vm.SelectedCustomers.Clear(); vm.SelectedSuppliers.Clear();
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 40 });
        Assert.AreEqual("old", last!.Keyword); CollectionAssert.AreEqual(new[] { "C1" }, last.CustomerCodes); CollectionAssert.AreEqual(new[] { supplier }, last.SupplierIds);
        Assert.AreEqual(2, vm.PageIndex); Assert.AreEqual(40, vm.PageSize); var row = vm.Orders[0];
        fixture.Manager.SearchHandler = _ => Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("失败"));
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 3, NewSize = 80 });
        Assert.AreSame(row, vm.Orders[0]); Assert.AreEqual(2, vm.PageIndex); Assert.AreEqual(40, vm.PageSize);
        await vm.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("old", last.Keyword);
    }

    /// <summary>子表按采购身份全量加载，忽略取消的旧响应及释放后的响应不得回写。</summary>
    [TestMethod]
    public async Task ChildRows_FullLoadRejectsLateAndDisposedResponses()
    {
        var first = Order("PO-A"); var second = Order("PO-B"); var fixture = CreateFixture(first, second); var vm = fixture.ViewModel;
        var old = new TaskCompletionSource<ServiceResult<List<PurchaseOrderManagerLineDto>>>(); CancellationToken oldToken = default;
        // 记录真实取消Token，接口仍故意忽略它，验证提交前的身份门禁。
        fixture.Manager.LinesHandler = (id, token) =>
        {
            if (id == first.PurchaseOrderId) { oldToken = token; return old.Task; }
            return Task.FromResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok(Enumerable.Range(0, 302).Select(_ => new PurchaseOrderManagerLineDto { PurchaseOrderLineId = Guid.NewGuid(), Sku = "new" }).ToList()));
        };
        await vm.InitializeAsync(); vm.SelectedOrder = vm.Orders[1]; Assert.IsTrue(oldToken.IsCancellationRequested); Assert.AreEqual(302, vm.Lines.Count);
        old.SetResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok([new() { Sku = "old" }])); await Task.Yield(); Assert.AreEqual(302, vm.Lines.Count); Assert.IsTrue(vm.Lines.All(x => x.Sku == "new"));
        var late = new TaskCompletionSource<ServiceResult<List<PurchaseOrderManagerLineDto>>>(); fixture.Manager.LinesHandler = (_, _) => late.Task;
        vm.SelectedOrder = vm.Orders[0]; vm.Dispose(); late.SetResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok([new() { Sku = "disposed" }])); await Task.Yield(); Assert.AreEqual(0, vm.Lines.Count);
    }

    /// <summary>可见行全选和批量提交均排除已隐藏行，不把本地筛选误作全页选中。</summary>
    [TestMethod]
    public async Task VisibleSelection_OnlyVisibleCheckedOrdersAreSubmitted()
    {
        var fixture = CreateFixture(Order("PO-VISIBLE"), Order("PO-HIDDEN")); var vm = fixture.ViewModel; await vm.InitializeAsync();
        vm.UpdateVisibleOrders(vm.Orders.Take(1)); vm.IsAllCurrentPageChecked = true;
        Assert.IsTrue(vm.Orders[0].IsChecked); Assert.IsFalse(vm.Orders[1].IsChecked);
        vm.Orders[1].IsChecked = true; Assert.AreEqual(1, vm.SelectedCount);
        await vm.BatchConfirmCommand.ExecuteAsync(null); Assert.AreEqual(1, fixture.Orders.ConfirmCalls.Count);
    }

    /// <summary>未知、零和作废不适用的关联量有不同含义，使用服务端完整来源摘要。</summary>
    [TestMethod]
    public void LineSummary_NullZeroAndNotApplicableRemainDistinct()
    {
        var unknown = new PurchaseOrderManagerLineDto { AssociationStatus = PurchaseDemandAssociationStatusEnum.NeedsReview,
            AssociationReviewReason = "贡献不守恒", SourceSummary = "2条成品需求 · 多来源" };
        var row = unknown.Adapt<PurchaseOrderManagementLineModel>(); Assert.AreEqual("需核实", row.LinkedDemandQtyText);
        Assert.AreEqual(unknown.SourceSummary, row.SourceSummary); StringAssert.Contains(row.ReviewText, "贡献不守恒");
        var zero = new PurchaseOrderManagerLineDto { LinkedDemandQty = 0m, UnlinkedQty = 2m, AssociationStatus = PurchaseDemandAssociationStatusEnum.Unassociated }.Adapt<PurchaseOrderManagementLineModel>();
        Assert.AreEqual("0", zero.LinkedDemandQtyText);
        var inactive = new PurchaseOrderManagerLineDto { AssociationStatus = PurchaseDemandAssociationStatusEnum.NotApplicable }.Adapt<PurchaseOrderManagementLineModel>();
        Assert.AreEqual("不适用", inactive.LinkedDemandQtyText); Assert.AreEqual("不适用", inactive.UnlinkedQtyText); StringAssert.Contains(inactive.ReviewText, "采购已作废");
    }
}

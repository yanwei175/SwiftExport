using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Features.Inventory.Inbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Inbounds.Views;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证独立选源组件的勾选及真实模板绑定，不调用后端。</summary>
[TestClass]
public sealed class InboundSourcePickerTests
{
    /// <summary>当前入库身份仅用于其他草稿提示，查询和翻页持续携带，不增加仓库条件。</summary>
    [TestMethod]
    public async Task Initialize_PreservesCurrentInboundForWarningExclusion()
    {
        var api = new SourceApiStub();
        using var vm = new InboundSourcePickerViewModel(api, new RunnerStub(), new ReferenceDataCacheServiceStub());
        var id = Guid.NewGuid();
        await vm.InitializeAsync(new() { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, CurrentInboundDocumentId = id }, CancellationToken.None);
        Assert.AreEqual(id, api.Requests.Single().CurrentInboundDocumentId);
        await vm.PageChangeCommand.ExecuteAsync(2);
        Assert.AreEqual(id, api.Requests.Last().CurrentInboundDocumentId);
    }

    /// <summary>条件编辑不能锁住已有结果或让用户失去确认已有勾选的出口。</summary>
    [TestMethod]
    public void FilterChanges_PreserveSelectionAndConfirmation()
    {
        using var vm = new InboundSourcePickerViewModel();
        var row = new InboundSourcePickerRowModel { IsChecked = true };
        vm.LoadItems([row]);
        vm.Keyword = "新条件";
        Assert.IsTrue(vm.CanSelect);
        vm.SelectedSupplier = new SupplierDto { Uid = Guid.NewGuid() };
        Assert.IsTrue(vm.IsPrimaryButtonEnabled);
        vm.PurchaseCategory = InboundPurchaseCategoryEnum.Normal;
        Assert.IsTrue(vm.CanSelect);
        Assert.IsTrue(vm.IsPrimaryButtonEnabled);
        Assert.AreSame(row, vm.GetCheckedItems().Single());
    }

    /// <summary>仅显式查询应用新条件；翻页保持成功结果的条件，查询失败保留旧结果可选。</summary>
    [TestMethod]
    public async Task Search_AppliesFiltersExplicitly_AndRestoresSelectionAfterFailure()
    {
        var api = new SourceApiStub();
        using var vm = new InboundSourcePickerViewModel(api, new RunnerStub(), new ReferenceDataCacheServiceStub());
        await vm.SearchCommand.ExecuteAsync(null);
        var oldRow = vm.Items.Single();
        vm.Keyword = "新条件";
        vm.PurchaseCategory = InboundPurchaseCategoryEnum.Normal;
        Assert.AreEqual(1, api.Requests.Count);
        await vm.PageChangeCommand.ExecuteAsync(2);
        Assert.AreEqual(string.Empty, api.Requests.Last().Keyword);
        Assert.AreEqual(InboundPurchaseCategoryEnum.CustomerSupplied, api.Requests.Last().PurchaseCategory);
        api.Pending = new();
        var search = vm.SearchCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.CanSelect);
        Assert.AreEqual("新条件", api.Requests.Last().Keyword);
        Assert.AreEqual(InboundPurchaseCategoryEnum.Normal, api.Requests.Last().PurchaseCategory);
        vm.Keyword = "尚未查询";
        api.Pending.SetResult(ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>.Fail("测试失败"));
        await search;
        Assert.IsTrue(vm.CanSelect);
        Assert.AreEqual(oldRow.SourceLineId, vm.Items.Single().SourceLineId);
        vm.Items.Single().IsChecked = true;
        Assert.IsTrue(vm.IsPrimaryButtonEnabled);
    }

    /// <summary>仅记录查询快照，允许测试控制响应时间。</summary>
    private sealed class SourceApiStub : IInboundSourceApi
    {
        private readonly Guid _source = Guid.NewGuid();
        public List<InboundSourceSearchRequest> Requests { get; } = [];
        public TaskCompletionSource<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>>? Pending { get; set; }
        /// <inheritdoc/>
        public Task<ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>> SearchAsync(InboundSourceSearchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Pending?.Task ?? Task.FromResult(ServiceResult<PagedResponseBase<InboundSourceCandidateDto>>.Ok(new()
            {
                Items = [new() { DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, SourceLineId = _source }],
                PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 100
            }));
        }
    }

    /// <summary>测试直接执行请求，不引入 HTTP 或全局消息。</summary>
    private sealed class RunnerStub : IApiCallRunner
    {
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        /// <inheritdoc/>
        public Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => throw new NotSupportedException();
    }

    /// <summary>全选和取消只改变勾选，不修改来源数量；空集合可安全操作。</summary>
    [TestMethod]
    public void ToggleAll_PreservesQuantities()
    {
        var first = new InboundSourcePickerRowModel { IsChecked = true, OriginalQty = 100, RemainingReceivableQty = 30 };
        var vm = new InboundSourcePickerViewModel();
        vm.LoadItems([first, new InboundSourcePickerRowModel()]);
        Assert.AreEqual(1, vm.GetCheckedItems().Count);
        vm.ToggleAllCommand.Execute(null);
        Assert.AreEqual(2, vm.GetCheckedItems().Count);
        vm.ToggleAllCommand.Execute(null);
        Assert.AreEqual(0, vm.GetCheckedItems().Count);
        Assert.AreEqual(100m, first.OriginalQty);
        Assert.AreEqual(30m, first.RemainingReceivableQty);
        vm.LoadItems([]);
        vm.ToggleAllCommand.Execute(null);
        Assert.AreEqual(0, vm.GetCheckedItems().Count);
    }

    /// <summary>真实模板的复选框回写行模型，运行时不自动注入设计时数据。</summary>
    [TestMethod]
    public void View_CheckboxBinding_WritesSelection()
    {
        StaTest.RunAsync(() =>
        {
            var view = new InboundSourcePickerView();
            Assert.IsNull(view.DataContext);
            var grid = ((Grid)view.Content).Children.OfType<DataGrid>().Single();
            var column = (DataGridTemplateColumn)grid.Columns[0];
            var checkBox = (CheckBox)column.CellTemplate.LoadContent();
            var row = new InboundSourcePickerRowModel();
            checkBox.DataContext = row;
            checkBox.GetBindingExpression(ToggleButton.IsCheckedProperty)!.UpdateTarget();
            checkBox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            Assert.IsTrue(row.IsChecked);
            return Task.CompletedTask;
        });
    }
}

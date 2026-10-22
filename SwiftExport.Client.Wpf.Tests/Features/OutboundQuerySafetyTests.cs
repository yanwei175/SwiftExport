using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Inventory.Outbounds.Services;
using SwiftExportClient.Features.Inventory.Outbounds.ViewModels;
using SwiftExportClient.Features.Inventory.Outbounds.Views;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Resources.Controls.Filter;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features;

/// <summary>验证出库查询快照、输入边界、独立仓库供数及真实 View 选择事件。</summary>
public sealed partial class OutboundManagerQueryTests
{
    /// <summary>多选复制为独立请求；刷新和翻页沿用已应用条件，清空后显式查询不限。</summary>
    [TestMethod]
    public async Task MultiFilters_FreezeInFlightAndAppliedSnapshot()
    {
        var api = new ApiStub { Pending = new() }; using var vm = Create(api);
        vm.SelectedDocumentTypes.Add(OutboundDocumentTypeEnum.FinalCustomerShipment); vm.SelectedPostingStatuses.Add(OutboundPostingStatusEnum.Posted);
        var search = vm.SearchCommand.ExecuteAsync(null); var original = api.Requests.Single();
        vm.SelectedDocumentTypes.Clear(); vm.SelectedPostingStatuses.Clear();
        Assert.AreEqual(2, original.DocumentTypes.Count); Assert.AreEqual(2, original.PostingStatuses.Count);
        api.Pending.SetResult(ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>.Ok(new() { Items = [] })); await search; api.Pending = null;
        await vm.RefreshCommand.ExecuteAsync(null); await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 2, NewSize = 50 });
        CollectionAssert.AreEqual(original.DocumentTypes, api.Requests.Last().DocumentTypes);
        CollectionAssert.AreEqual(original.PostingStatuses, api.Requests.Last().PostingStatuses);
        await vm.SearchCommand.ExecuteAsync(null); Assert.AreEqual(0, api.Requests.Last().DocumentTypes.Count); Assert.AreEqual(0, api.Requests.Last().PostingStatuses.Count);
    }

    /// <summary>200 字可查、201 字按钮及直接入口阻断；纠正错误不覆盖较新反馈，非法分页不发送。</summary>
    [TestMethod]
    public async Task QueryLimits_KeepRowsAndLaterFeedback()
    {
        var api = new ApiStub(); using var vm = Create(api); vm.Keyword = new string('字', 200);
        await vm.SearchCommand.ExecuteAsync(null); var row = vm.SelectedItem;
        vm.Keyword = new string('字', 201); Assert.IsFalse(vm.SearchCommand.CanExecute(null)); await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.Requests.Count); Assert.AreSame(row, vm.SelectedItem);
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 2, NewSize = 50 }); Assert.AreEqual(200, api.Requests.Last().Keyword!.Length);
        vm.ResultSummary = "后来反馈"; vm.Keyword = "修正"; Assert.AreEqual("后来反馈", vm.ResultSummary);
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 3, NewSize = 201 }); Assert.AreEqual(2, api.Requests.Count);
    }

    /// <summary>读取时产生的真实输入不能被响应覆盖；详情新选择替代旧请求。</summary>
    [TestMethod]
    public async Task Query_DoesNotOverwriteLateInputOrNewSelection()
    {
        var api = new ApiStub(); using var vm = Create(api); await vm.SearchCommand.ExecuteAsync(null);
        var first = vm.SelectedItem!; api.Pending = new(); var search = vm.SearchCommand.ExecuteAsync(null);
        first.Remark = "晚到输入"; api.Pending.SetResult(ServiceResult<PagedResponseBase<OutboundManagerListItemDto>>.Ok(new() { Items = [] })); await search;
        Assert.AreSame(first, vm.SelectedItem); Assert.AreEqual("晚到输入", first.Remark);
        var pending = new TaskCompletionSource<ServiceResult<OutboundManagerDetailDto>>(); first.IsDetailLoaded = false;
        api.DetailHandler = uid => uid == first.Uid ? pending.Task : Task.FromResult(DetailResult(uid));
        var old = vm.LoadSelectedDetailCommand.ExecuteAsync(null); var second = vm.Headers.Items[1];
        vm.SelectedItem = second; await vm.LoadSelectedDetailCommand.ExecuteAsync(null);
        pending.SetResult(DetailResult(first.Uid)); await old;
        Assert.AreSame(second, vm.SelectedItem); Assert.IsTrue(second.IsDetailLoaded); Assert.IsFalse(first.IsDetailLoaded);
    }

    /// <summary>单据删除后选到未加载摘要时，写 Busy 退出后自动读取当前详情。</summary>
    [TestMethod]
    public async Task Delete_LoadsSelectedPreviousSummary()
    {
        var api = new ApiStub { AllowDelete = true };
        using var vm = new OutboundManagerViewModel(new WarehouseStub(), new SourceStub(), new DialogStub(), api, new RunnerStub(),
            new OutboundPersistenceService(api, Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboundPersistenceService>.Instance),
            new ConfirmDeletion(), postingUser: OutboundTestUsers.Create());
        await vm.SearchCommand.ExecuteAsync(null); var first = vm.SelectedItem!; vm.SelectedItem = vm.Headers.Items[1];
        await vm.LoadSelectedDetailCommand.ExecuteAsync(null); first.IsDetailLoaded = false;
        await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreSame(first, vm.SelectedItem); Assert.IsTrue(first.IsDetailLoaded); Assert.AreEqual(3, api.DetailCalls); Assert.IsFalse(vm.IsBusy);
    }

    /// <summary>出库候选专用接口取完超过 200 条仓库，不要求管理查询或共享缓存。</summary>
    [TestMethod]
    public async Task WarehouseOptions_LoadAllPagesThroughOutboundEndpoint()
    {
        var api = new ApiStub(); var pages = new List<int>();
        var options = Enumerable.Range(1, 201).Select(i => new WarehouseSitePickerDto { Uid = Guid.NewGuid(), Name = $"仓库{i}", IsActive = true }).ToList();
        api.OptionsHandler = request =>
        {
            pages.Add(request.PageIndex); Assert.AreEqual(OutboundQueryLimits.PageSizeMax, request.PageSize);
            return Task.FromResult(ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Ok(new()
            { Items = options.Skip((request.PageIndex - 1) * request.PageSize).Take(request.PageSize).ToList(), TotalCount = 201 }));
        };
        var service = new OutboundWarehousePickerService(api, new RunnerStub(), new DialogStub());
        using var picker = new SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels.WarehousePickerViewModel(service.LoadOptionsAsync);
        await picker.OpenAsync(); Assert.AreEqual(201, picker.TotalCount); CollectionAssert.AreEqual(new[] { 1, 2 }, pages);
        picker.PageChangeCommand.Execute(new PageChangeRequest { NewIndex = 2, NewSize = 200 }); Assert.AreEqual(options.Last().Uid, picker.Items.Single().Uid);
        Assert.AreEqual(0, api.Requests.Count);
    }

    /// <summary>真实界面回填只加载一次详情；多选弹窗中文搜索可见候选，取消不改已应用选择。</summary>
    [TestMethod]
    public void View_QueryAndChineseMultiSelectKeepSelection()
    {
        Controls.DatePickerAssistTests.RunWithApplication(async () =>
        {
            ApplicationResourceLoader.Load(); var api = new ApiStub(); using var vm = Create(api);
            var view = new OutboundManagerView { DataContext = vm };
            var window = new Window { Content = view, Width = 1000, Height = 650, ShowInTaskbar = false, ShowActivated = false, Left = -10000, Top = -10000 };
            try
            {
                window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); Assert.AreEqual(1, api.DetailCalls);
                foreach (var (name, text) in new[] { ("DocumentTypesFilter", "普通"), ("PostingStatusesFilter", "未过账") })
                {
                    var filter = (MultiSelectLookupButton)view.FindName(name); var popup = (Popup)filter.FindName("PART_Popup");
                    popup.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    ((TextBox)filter.FindName("PART_SearchBox")).Text = text; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                    var list = (ListBox)filter.FindName("PART_ListBox"); Assert.AreEqual(1, list.Items.Count);
                    var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0); Assert.IsNotNull(item);
                    var check = VisualChildren<ToggleButton>(item).Single(); Assert.IsTrue(check.IsChecked == true); check.IsChecked = false;
                    popup.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                }
                CollectionAssert.AreEqual(new[] { OutboundDocumentTypeEnum.StockIssue }, vm.SelectedDocumentTypes.ToArray());
                CollectionAssert.AreEqual(new[] { OutboundPostingStatusEnum.Unposted }, vm.SelectedPostingStatuses.ToArray());
            }
            finally { window.Close(); }
        });
    }

    /// <summary>提供稳定归属与有效版本的查询详情。</summary>
    private static ServiceResult<OutboundManagerDetailDto> DetailResult(Guid uid) => ServiceResult<OutboundManagerDetailDto>.Ok(new() { Header = new() { Uid = uid, Version = 3 }, Lines = [] });
    /// <summary>读取候选容器的真实视觉控件，不反射私有候选模型。</summary>
    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i); if (child is T match) yield return match;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
    /// <summary>为持久化删除提供共同原因。</summary>
    private sealed class ConfirmDeletion : IOutboundDeleteDialogService
    {
        /// <inheritdoc/>
        public Task<string?> ConfirmAsync(int count, CancellationToken token) => Task.FromResult<string?>("测试删除");
    }
}

/// <summary>验证库存选品的关键词、分页与排除集合边界。</summary>
public sealed partial class OutboundItemPickerTests
{
    /// <summary>200 字和500个排除项允许，201字或501项直接调用也不发送；失败保留旧候选。</summary>
    [TestMethod]
    public async Task PickerLimits_RejectInvalidRequestsAndKeepFeedback()
    {
        var api = DispatchProxy.Create<SwiftExport.Contracts.Apis.Inventory.IOutboundManagerApi, ApiProxy>();
        var proxy = (ApiProxy)(object)api; var requests = new List<OutboundItemSearchRequest>(); var warehouse = Guid.NewGuid();
        proxy.Handler = request => { requests.Add(request); return Task.FromResult(Result(warehouse, "候选")); };
        using var vm = new OutboundItemPickerViewModel(api, new Runner());
        await vm.InitializeAsync(new() { WarehouseSiteId = warehouse, ExcludedProductIds = Enumerable.Range(1, 500).Select(_ => Guid.NewGuid()).ToArray() }, CancellationToken.None);
        vm.Keyword = new string('字', 200); await vm.SearchCommand.ExecuteAsync(null); var row = vm.Items.Single();
        vm.Keyword = new string('字', 201); Assert.IsFalse(vm.SearchCommand.CanExecute(null)); await vm.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(2, requests.Count); Assert.AreSame(row, vm.Items.Single());
        proxy.Handler = request => { requests.Add(request); return Task.FromResult(ServiceResult<PagedResponseBase<OutboundItemCandidateDto>>.Fail("后来的反馈")); };
        await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 50 }); vm.Keyword = "修正";
        Assert.AreEqual("后来的反馈", vm.StatusText); await vm.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 201 });
        Assert.AreEqual(3, requests.Count);
        using var invalid = new OutboundItemPickerViewModel(api, new Runner());
        await invalid.InitializeAsync(new() { WarehouseSiteId = warehouse, ExcludedProductIds = Enumerable.Range(1, 501).Select(_ => Guid.NewGuid()).ToArray() }, CancellationToken.None);
        Assert.AreEqual(3, requests.Count); StringAssert.Contains(invalid.StatusText, "500");
    }
}

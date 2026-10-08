using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.QualityControl.FlowManagement;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExportClient.Features.QualityControl.FlowManagement.Models;
using SwiftExportClient.Features.QualityControl.FlowManagement.ViewModels;
using SwiftExportClient.Navigation;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.FlowManagement;

/// <summary>验证分页筛选、替代请求、权限和文档导航。</summary>
[TestClass]
public sealed class FlowManagerTests
{
    /// <summary>迟到的旧查询不能覆盖新筛选结果或页码。</summary>
    [TestMethod]
    public async Task LatestSearchWinsEvenWhenOldApiIgnoresCancellation()
    {
        var host = new FlowTestHost(); using var vm = host.Manager();
        var old = new TaskCompletionSource<ServiceResult<PagedResponseBase<FlowListItemDto>>>();
        CancellationToken captured = default;
        host.Api.SearchOverride = (request, token) => { captured = token; return old.Task; };
        vm.Sku = "旧"; var first = vm.SearchCommand.ExecuteAsync(null);
        host.Api.SearchOverride = (request, token) => Task.FromResult(ServiceResult<PagedResponseBase<FlowListItemDto>>.Ok(
            new() { Items = [new() { Uid = Guid.NewGuid(), SkuSnapshot = request.SkuSnapshot }], TotalCount = 45, PageIndex = 1, PageSize = 20 }));
        vm.Sku = "新"; await vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(captured.IsCancellationRequested);
        old.SetResult(ServiceResult<PagedResponseBase<FlowListItemDto>>.Ok(
            new() { Items = [new() { SkuSnapshot = "旧" }], TotalCount = 100, PageIndex = 9, PageSize = 20 }));
        await first; Assert.AreEqual("新", vm.Groups[0].SkuSnapshot); Assert.AreEqual(1, vm.PageIndex); Assert.AreEqual(45, vm.TotalCount);
    }

    /// <summary>物料、SKU 和日期作为独立筛选传输，失败翻页不改变当前页。</summary>
    [TestMethod]
    public async Task FiltersAndFailedPageStayConsistent()
    {
        var host = new FlowTestHost(); using var vm = host.Manager();
        vm.SelectedMaterial = new ProductLookupDto { Uid = FlowTestHost.MaterialId };
        vm.Sku = " SKU-1 "; vm.DateFrom = new(2026, 10, 1); vm.DateTo = new(2026, 10, 8);
        host.Api.SearchOverride = (request, token) => Task.FromResult(ServiceResult<PagedResponseBase<FlowListItemDto>>.Ok(
            new() { Items = [new() { SkuSnapshot = "SKU-1" }], TotalCount = 50, PageIndex = request.PageIndex, PageSize = 20 }));
        await vm.InitializeAsync();
        Assert.AreEqual(FlowTestHost.MaterialId, host.Api.LastSearch!.MaterialId);
        Assert.AreEqual("SKU-1", host.Api.LastSearch.SkuSnapshot); Assert.AreEqual(vm.DateTo, host.Api.LastSearch.TestDateTo);
        host.Api.SearchOverride = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<FlowListItemDto>>.Fail("读取失败"));
        await vm.NextPageCommand.ExecuteAsync(null); Assert.AreEqual(1, vm.PageIndex); Assert.AreEqual(1, vm.Groups.Count);
    }

    /// <summary>同一已保存组使用相同页签身份，每次新建使用独立临时身份。</summary>
    [TestMethod]
    public async Task NavigationUsesTypedDocumentKeys()
    {
        var host = new FlowTestHost(); using var vm = host.Manager(); await vm.InitializeAsync();
        await vm.OpenCommand.ExecuteAsync(null); var key = host.Navigation.Key;
        Assert.AreEqual($"flow:{vm.SelectedGroup!.Uid:N}", key);
        await vm.OpenCommand.ExecuteAsync(null); Assert.AreEqual(key, host.Navigation.Key);
        await vm.CreateCommand.ExecuteAsync(null); var first = host.Navigation.Key;
        await vm.CreateCommand.ExecuteAsync(null); Assert.AreNotEqual(first, host.Navigation.Key);
        Assert.IsTrue(host.Navigation.Key!.StartsWith("flow:new:"));
        var registry = new NavigationRouteRegistry();
        Assert.IsTrue(registry.TryGetRoute(FlowManagerViewModel.RouteKey, out var route));
        Assert.AreEqual(PermissionKeys.QualityControlFlowView, route.RequiredPermission);
        Assert.IsTrue(registry.TryGetRoute(FlowDetailViewModel.RouteKey, out route));
        Assert.AreEqual(PermissionKeys.QualityControlFlowView, route.RequiredPermission);
    }

    /// <summary>释放后拒绝迟到查询，权限撤回清除已经加载的敏感列表。</summary>
    [TestMethod]
    public async Task DisposalAndAccessRevocationClearOrRejectResults()
    {
        var host = new FlowTestHost(); using var vm = host.Manager(); await vm.InitializeAsync();
        Assert.AreEqual(1, vm.Groups.Count); host.User.Clear(); Assert.AreEqual(0, vm.Groups.Count);
        Assert.IsFalse(vm.OpenCommand.CanExecute(null));
        var fresh = new FlowTestHost(); var gate = new TaskCompletionSource<ServiceResult<PagedResponseBase<FlowListItemDto>>>();
        CancellationToken captured = default;
        fresh.Api.SearchOverride = (_, token) => { captured = token; return gate.Task; };
        var disposed = fresh.Manager(); var pending = disposed.InitializeAsync(); disposed.Dispose();
        Assert.IsTrue(captured.IsCancellationRequested);
        gate.SetResult(ServiceResult<PagedResponseBase<FlowListItemDto>>.Ok(new() { Items = [new() { SkuSnapshot = "迟到" }], TotalCount = 1 }));
        await pending; Assert.AreEqual(0, disposed.Groups.Count);
    }

    /// <summary>已有停用项可保持；新行即使取得历史候选身份，也必须被阻断。</summary>
    [TestMethod]
    public async Task NewLineCannotReuseDisabledHistoricalCandidate()
    {
        var dictionary = FlowTestHost.Dictionaries().ToArray(); dictionary[0].IsEnabled = false;
        var host = new FlowTestHost(dictionary); using var vm = host.Editor();
        await vm.InitializeDocumentAsync(new FlowOpenRequest { HeaderId = host.Api.Detail.Uid }, default);
        vm.SelectedLine = vm.Lines[0]; vm.RemoveLineCommand.Execute(null);
        vm.AddLineCommand.Execute(null); vm.SelectedLine!.FunctionId = FlowTestHost.FunctionId; vm.SelectedLine.Flow1BarLMin = 3;
        await vm.SaveCommand.ExecuteAsync(null); Assert.AreEqual(0, host.Api.SaveCalls); Assert.IsTrue(vm.IsDirty);
    }
}


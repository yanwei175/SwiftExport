using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Navigation;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.DemandPlanning.GrossDemand;
public sealed partial class GrossDemandWorkspaceViewModelTests
{
    /// <summary>忽略取消的旧查询不能覆盖新查询，客户与来源候选也不能跨会话回写。</summary>
    [TestMethod]
    public async Task Reads_RejectLateQueriesAndSessionCandidates()
    {
        var api = new FakeGrossDemandApi(); var old = new TaskCompletionSource<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>>(); var user = GrossUser();
        api.SearchHandler = (_, _) => old.Task; using var vm = CreateViewModel(api, currentUser: user); var first = vm.SearchCommand.ExecuteAsync(null);
        api.SearchHandler = (_, _) => Task.FromResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(CreatePage(CreateItem("LATEST", false)))); vm.Keyword = "latest"; await vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(api.SearchTokens[0].IsCancellationRequested); old.SetResult(ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(CreatePage(CreateItem("OLD", false)))); await first; Assert.AreEqual("LATEST", vm.Items[0].SourceNo);
        var customers = new TaskCompletionSource<ServiceResult<List<GrossDemandCustomerOptionDto>>>(); api.CustomerHandler = _ => customers.Task; var customerRead = vm.RefreshCustomerOptionsCommand.ExecuteAsync(null);
        var groups = new TaskCompletionSource<ServiceResult<List<GrossDemandSourceGroupDto>>>(); api.SourceHandler = (_, _) => groups.Task; var groupRead = vm.ToggleSourcePanelCommand.ExecuteAsync(null);
        user.SetCurrentUser(new MeDto { UserId = "other", Permissions = [PermissionKeys.DemandPlanningSourceView] });
        customers.SetResult(ServiceResult<List<GrossDemandCustomerOptionDto>>.Ok([new() { Id = Guid.NewGuid(), Code = "OLD" }])); groups.SetResult(ServiceResult<List<GrossDemandSourceGroupDto>>.Ok([new() { SourceUid = Guid.NewGuid(), SourceNo = "OLD" }]));
        await Task.WhenAll(customerRead, groupRead); Assert.AreEqual(0, vm.Items.Count); Assert.AreEqual(0, vm.CustomerOptions.Count); Assert.AreEqual(0, vm.SourceGroups.Count);
    }

    /// <summary>跟踪和CR来源使用各自权限，普通成品需求查看不授予维护或下游读取能力。</summary>
    [TestMethod]
    public async Task Navigation_UsesTrackingAndAfterSalesPermissions()
    {
        var row = CreateItem("CR-1", false); row.SourceType = "CR"; var api = new FakeGrossDemandApi { SearchResponse = CreatePage(row) }; var user = GrossUser(PermissionKeys.QualityControlAfterSalesView, PermissionKeys.OrderTrackingView);
        var nav = DispatchProxy.Create<INavigationService, GrossNavigationRecorder>(); var record = (GrossNavigationRecorder)(object)nav;
        using var vm = CreateViewModel(api, currentUser: user, navigationService: nav); await vm.InitializeAsync(); Assert.IsFalse(vm.EditSelectedManualCommand.CanExecute(null)); Assert.IsTrue(vm.OpenTraceCommand.CanExecute(null));
        await vm.OpenTraceCommand.ExecuteAsync(null); Assert.AreEqual("demand-tracking-detail", record.Requests[0].Route); Assert.AreEqual(row.Uid.ToString("N"), record.Requests[0].Key); Assert.AreEqual(row.Uid, record.Requests[0].Parameter);
        await vm.OpenSourceCommand.ExecuteAsync(null); Assert.AreEqual("quality-control-after-sales-detail", record.Requests[1].Route);
        using var denied = CreateViewModel(api, currentUser: GrossUser()); await denied.InitializeAsync(); Assert.IsFalse(denied.OpenSourceCommand.CanExecute(null)); Assert.IsFalse(denied.OpenTraceCommand.CanExecute(null));
        Assert.IsFalse(new NavigationRouteRegistry().TryGetRoute("gross-demand-trace", out _));
    }

    private static CurrentUserService GrossUser(params string[] permissions)
    {
        var user = new CurrentUserService(); user.SetCurrentUser(new MeDto { UserId = "u", Permissions = new[] { PermissionKeys.DemandPlanningSourceView }.Concat(permissions).ToList() }); return user;
    }
    public class GrossNavigationRecorder : DispatchProxy
    {
        public List<(string Route, string Key, object? Parameter)> Requests { get; } = [];
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        { if (method?.Name == "OpenDocumentAsync") { Requests.Add(((string)args![0]!, (string)args[1]!, args[2])); return Task.CompletedTask; } throw new NotSupportedException(method?.Name); }
    }
}

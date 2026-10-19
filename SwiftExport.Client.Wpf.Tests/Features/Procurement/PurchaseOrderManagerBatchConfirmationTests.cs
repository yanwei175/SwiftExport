using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.DTOs.Products.Execution;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Models;
using SwiftExportClient.Features.Procurement.PurchaseOrders.Services;
using SwiftExportClient.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Procurement;

/// <summary>验证采购管理页当前页选择与批量确认的客户端安全边界；所有接口均为内存替身。</summary>
[TestClass]
public sealed partial class PurchaseOrderManagerBatchConfirmationTests
{
    /// <summary>初始待确认、客户供应商精确身份及日期必须完整传递；清空筛选解除所有限制。</summary>
    [TestMethod]
    public async Task FiltersUseSelectedIdentitiesAndClearAllRestrictions()
    {
        var fixture = CreateFixture(Order("PO-FILTER"));
        PurchaseOrderManagerSearchRequest? captured = null;
        fixture.Manager.SearchHandler = request =>
        {
            captured = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Ok(new() { Items = [], PageIndex = 1, PageSize = 20 }));
        };
        await fixture.ViewModel.InitializeAsync();
        CollectionAssert.AreEqual(new[] { ConfirmationStatusEnum.待确认 }, captured!.ConfirmationStatuses);
        var supplierId = Guid.NewGuid();
        fixture.ViewModel.SelectedCustomers.Add(new() { CustomerCode = "C-SELECTED" });
        fixture.ViewModel.SelectedSuppliers.Add(new() { Uid = supplierId });
        fixture.ViewModel.Keyword = "  PO-KEY  ";
        fixture.ViewModel.OrderDateFrom = new DateTime(2026, 9, 1);
        fixture.ViewModel.OrderDateTo = new DateTime(2026, 9, 16);
        await fixture.ViewModel.SearchCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { "C-SELECTED" }, captured.CustomerCodes);
        CollectionAssert.AreEqual(new[] { supplierId }, captured.SupplierIds);
        Assert.AreEqual("PO-KEY", captured.Keyword);
        Assert.AreEqual(new DateTime(2026, 9, 1), captured.OrderDateFrom);
        Assert.AreEqual(new DateTime(2026, 9, 16), captured.OrderDateTo);
        await fixture.ViewModel.ClearFiltersCommand.ExecuteAsync(null);
        Assert.IsNull(captured.CustomerCode); Assert.IsNull(captured.SupplierId);
        Assert.HasCount(0, captured.CustomerCodes); Assert.HasCount(0, captured.SupplierIds);
        Assert.IsNull(captured.OrderDateFrom); Assert.IsNull(captured.OrderDateTo);
        Assert.HasCount(0, captured.ConfirmationStatuses);
        Assert.AreEqual(string.Empty, captured.Keyword);
    }

    /// <summary>勾选、表头三态和查询失败不会改变主从选择或丢失当前页数据。</summary>
    [TestMethod]
    public async Task SelectionAndFailedRefreshMustPreserveCurrentPageState()
    {
        var fixture = CreateFixture(Order("PO-1"), Order("PO-2"));
        await fixture.ViewModel.InitializeAsync();
        var selectedOrder = fixture.ViewModel.SelectedOrder;

        fixture.ViewModel.Orders[0].IsChecked = true;
        var canExecuteChangedCount = 0;
        fixture.ViewModel.BatchConfirmCommand.CanExecuteChanged += (_, _) => canExecuteChangedCount++;
        fixture.ViewModel.Orders[0].IsChecked = false;
        fixture.ViewModel.Orders[0].IsChecked = true;
        Assert.AreEqual(1, fixture.ViewModel.SelectedCount);
        Assert.IsGreaterThan(0, canExecuteChangedCount);
        Assert.IsNull(fixture.ViewModel.IsAllCurrentPageChecked);
        Assert.AreSame(selectedOrder, fixture.ViewModel.SelectedOrder);

        fixture.ViewModel.IsAllCurrentPageChecked = true;
        Assert.IsTrue(fixture.ViewModel.Orders.All(static order => order.IsChecked));
        Assert.IsTrue(fixture.ViewModel.IsAllCurrentPageChecked == true);

        fixture.Manager.SearchHandler = _ => Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("查询失败"));
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);

        Assert.HasCount(2, fixture.ViewModel.Orders);
        Assert.AreEqual(2, fixture.ViewModel.SelectedCount);
        Assert.AreSame(selectedOrder, fixture.ViewModel.SelectedOrder);
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "查询失败");

        fixture.Manager.SearchHandler = null;
        await fixture.ViewModel.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(0, fixture.ViewModel.SelectedCount);
        Assert.IsTrue(fixture.ViewModel.Orders.All(static order => !order.IsChecked));
    }

    /// <summary>无勾选或无确认权限时禁用批量命令；执行期间查询命令禁用，取消确认不产生写请求。</summary>
    [TestMethod]
    public async Task PermissionBusyAndCancelledConfirmationMustBlockWrites()
    {
        var fixture = CreateFixture(Order("PO-1"));
        await fixture.ViewModel.InitializeAsync();
        Assert.IsFalse(fixture.ViewModel.BatchConfirmCommand.CanExecute(null));

        fixture.ViewModel.Orders[0].IsChecked = true;
        Assert.IsTrue(fixture.ViewModel.BatchConfirmCommand.CanExecute(null));
        var contextSource = new TaskCompletionSource<ServiceResult<ProcurementConfirmationContextDto>>();
        fixture.Orders.ContextHandler = _ => contextSource.Task;
        fixture.Dialogs.ConfirmResult = false;

        var execution = fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);
        Assert.IsTrue(fixture.ViewModel.IsBusy);
        Assert.IsFalse(fixture.ViewModel.SearchCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.RefreshCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.PageChangeCommand.CanExecute(new PageChangeRequest { NewIndex = 2, NewSize = 20 }));
        contextSource.SetResult(ServiceResult<ProcurementConfirmationContextDto>.Ok(Context(
            new PurchaseOrderManagerListItemDto { PurchaseOrderId = fixture.ViewModel.Orders[0].PurchaseOrderId }, 7, "fp-7")));
        await execution;

        Assert.HasCount(0, fixture.Orders.ConfirmCalls);
        Assert.IsFalse(fixture.ViewModel.IsBusy);
        Assert.IsTrue(fixture.ViewModel.BatchConfirmCommand.CanExecute(null));

        var readOnlyFixture = CreateFixture(new[] { Order("PO-2") }, includeConfirmPermission: false);
        await readOnlyFixture.ViewModel.InitializeAsync();
        readOnlyFixture.ViewModel.Orders[0].IsChecked = true;
        Assert.IsFalse(readOnlyFixture.ViewModel.BatchConfirmCommand.CanExecute(null));

        var noReadFixture = CreateFixture(new[] { Order("PO-3") }, includeConfirmPermission: true, includeReadPermission: false);
        noReadFixture.ViewModel.Orders.Add(new PurchaseOrderManagementRowModel { IsChecked = true, CanConfirm = true });
        Assert.IsFalse(noReadFixture.ViewModel.BatchConfirmCommand.CanExecute(null));
    }

    /// <summary>客供件不再因消耗绑定跳过；确认使用预检时固定的版本、指纹与命令身份。</summary>
    [TestMethod]
    public async Task CustomerSuppliedOrderConfirmsWithoutWarehouseBinding()
    {
        var skipped = Order("PO-CS");
        var contextFailure = Order("PO-CTX");
        var executable = Order("PO-OK");
        var fixture = CreateFixture(skipped, contextFailure, executable);
        fixture.Orders.ContextHandler = id => Task.FromResult(id == contextFailure.PurchaseOrderId
            ? ServiceResult<ProcurementConfirmationContextDto>.Fail("确认依据读取失败")
            : ServiceResult<ProcurementConfirmationContextDto>.Ok(
                id == skipped.PurchaseOrderId ? Context(skipped, 3, "fp-cs", customerSupplied: true) : Context(executable, 9, "fp-ok")));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsAllCurrentPageChecked = true;

        await fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);

        Assert.HasCount(2, fixture.Orders.ConfirmCalls);
        var call = fixture.Orders.ConfirmCalls.Single(x => x.OrderId == executable.PurchaseOrderId);
        Assert.AreEqual(executable.PurchaseOrderId, call.OrderId);
        Assert.AreEqual(9, call.Command.ExpectedVersion);
        Assert.AreEqual("fp-ok", call.Command.ContextFingerprint);
        Assert.AreNotEqual(Guid.Empty, call.Command.CommandId);
        Assert.IsFalse(string.IsNullOrWhiteSpace(call.Command.ContextFingerprint));
        StringAssert.Contains(fixture.Dialogs.LastConfirmationMessage, "PO-CS");
        Assert.IsFalse(fixture.Dialogs.LastConfirmationMessage.Contains("消耗绑定"));
        StringAssert.Contains(fixture.Dialogs.LastConfirmationMessage, "PO-CTX");
        StringAssert.Contains(fixture.Dialogs.LastConfirmationMessage, "确认依据读取失败");
        StringAssert.Contains(fixture.Dialogs.LastConfirmationMessage, "PO-OK · S-001");
    }

    /// <summary>明确冲突不阻断其他独立订单；成功项清除勾选，失败项刷新后仍保留。</summary>
    [TestMethod]
    public async Task StableConflictMustContinueAndPreserveOnlyNonSuccessfulSelection()
    {
        var first = Order("PO-1");
        var second = Order("PO-2");
        var fixture = CreateFixture(first, second);
        fixture.Orders.ConfirmHandler = (id, command) => Task.FromResult(id == first.PurchaseOrderId
            ? ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(id, command.ExpectedVersion))
            : ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.VersionConflict, "版本冲突"));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsAllCurrentPageChecked = true;

        await fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);

        Assert.HasCount(2, fixture.Orders.ConfirmCalls);
        Assert.IsFalse(fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == first.PurchaseOrderId).IsChecked);
        Assert.IsTrue(fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == second.PurchaseOrderId).IsChecked);
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "成功 1");
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "失败 1");
        Assert.IsFalse(fixture.ViewModel.ResultSummary.Contains('\n'));
        Assert.AreEqual(2, fixture.Manager.SearchCount);
    }

    /// <summary>无稳定错误码即视为未知并停止后续提交，未知项不能再次生成命令。</summary>
    [TestMethod]
    public async Task UnknownResultMustStopRemainingWritesAndMarkVerificationGate()
    {
        var first = Order("PO-1");
        var unknown = Order("PO-U");
        var remaining = Order("PO-3");
        var fixture = CreateFixture(first, unknown, remaining);
        // 使用真实稳定错误码，不能把结果未知当成普通业务拒绝后继续提交。
        fixture.Orders.ConfirmHandler = (id, command) => Task.FromResult(id == unknown.PurchaseOrderId
            ? ServiceResult<ProcurementOrderCommandResult>.Fail(ServiceErrorCodes.OutcomeUnknown, "传输结果不确定")
            : ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(id, command.ExpectedVersion)));
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.IsAllCurrentPageChecked = true;

        await fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);

        Assert.HasCount(2, fixture.Orders.ConfirmCalls);
        var unknownRow = fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == unknown.PurchaseOrderId);
        Assert.IsTrue(unknownRow.HasUnknownConfirmationResult);
        Assert.IsTrue(unknownRow.IsChecked);
        Assert.IsTrue(fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == remaining.PurchaseOrderId).IsChecked);
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "未知 1");
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "未执行 1");

        fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == remaining.PurchaseOrderId).IsChecked = false;
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        var refreshedUnknown = fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == unknown.PurchaseOrderId);
        Assert.IsTrue(refreshedUnknown.HasUnknownConfirmationResult);
        Assert.IsFalse(refreshedUnknown.IsChecked);
        refreshedUnknown.IsChecked = true;
        fixture.ViewModel.SelectedOrder = refreshedUnknown;
        Assert.IsFalse(fixture.ViewModel.ConfirmCommand.CanExecute(null));
        Assert.IsFalse(fixture.ViewModel.BatchConfirmCommand.CanExecute(null));
        await fixture.ViewModel.ConfirmCommand.ExecuteAsync(null);
        await fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);
        Assert.HasCount(2, fixture.Orders.ConfirmCalls);

        fixture.Manager.SearchHandler = request => Task.FromResult(Page(
            request.PageIndex == 2
                ? Array.Empty<PurchaseOrderManagerListItemDto>()
                : new[] { first, unknown, remaining },
            request.PageIndex,
            request.PageSize));
        await fixture.ViewModel.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 2, NewSize = 20 });
        Assert.HasCount(0, fixture.ViewModel.Orders);
        await fixture.ViewModel.PageChangeCommand.ExecuteAsync(new PageChangeRequest { NewIndex = 1, NewSize = 20 });
        Assert.IsTrue(fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == unknown.PurchaseOrderId).HasUnknownConfirmationResult);

        unknown.ConfirmationStatus = ConfirmationStatusEnum.已确认;
        unknown.CanConfirm = false;
        await fixture.ViewModel.RefreshCommand.ExecuteAsync(null);
        var confirmed = fixture.ViewModel.Orders.Single(order => order.PurchaseOrderId == unknown.PurchaseOrderId);
        Assert.AreEqual(ConfirmationStatusEnum.已确认, confirmed.ConfirmationStatus);
        Assert.IsFalse(confirmed.HasUnknownConfirmationResult);
        Assert.AreEqual(string.Empty, confirmed.BatchConfirmationStatus);
    }

    /// <summary>最终刷新失败不覆盖逐单执行结果，已确认成功的旧行也立即取消勾选。</summary>
    [TestMethod]
    public async Task FailedFinalRefreshMustRetainExecutionSummaryAndSuccessSelectionState()
    {
        var order = Order("PO-1");
        var fixture = CreateFixture(order);
        await fixture.ViewModel.InitializeAsync();
        fixture.ViewModel.Orders[0].IsChecked = true;
        fixture.Manager.SearchHandler = _ => Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("刷新失败"));

        await fixture.ViewModel.BatchConfirmCommand.ExecuteAsync(null);

        Assert.IsFalse(fixture.ViewModel.Orders[0].IsChecked);
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "成功 1");
        StringAssert.Contains(fixture.ViewModel.ResultSummary, "服务端状态刷新失败");
        Assert.HasCount(1, fixture.Orders.ConfirmCalls);
    }

    private static Fixture CreateFixture(params PurchaseOrderManagerListItemDto[] orders) => CreateFixture(orders, true);

    /// <summary>创建完全内存化的采购管理页测试夹具。</summary>
    private static Fixture CreateFixture(
        PurchaseOrderManagerListItemDto[] orders,
        bool includeConfirmPermission,
        bool includeReadPermission = true, bool includeEditPermission = false)
    {
        var manager = new ManagerApiStub(orders);
        var orderApi = new OrderApiStub(orders);
        var dialogs = new DialogServiceStub();
        var permissions = new List<string>();
        if (includeReadPermission) permissions.Add(PermissionKeys.ProcurementOrderRead);
        if (includeConfirmPermission) permissions.Add(PermissionKeys.ProcurementOrderConfirm);
        if (includeEditPermission) permissions.Add(PermissionKeys.ProcurementOrderEdit);
        var session = new ProcurementCommandSession();
        var viewModel = new PurchaseOrderManagerViewModel(manager, orderApi, new ProcurementDialogStub(),
            new NavigationStub(), dialogs, new ApiCallRunnerStub(), new MessageStub(),
            new CurrentUserStub(permissions), commandSession: session);
        return new Fixture(viewModel, manager, orderApi, dialogs, session);
    }

    private static PurchaseOrderManagerListItemDto Order(string orderNo) => new()
    {
        PurchaseOrderId = Guid.NewGuid(), OrderNo = orderNo, CustomerCode = "C-001", SupplierName = "S-001",
        ConfirmationStatus = ConfirmationStatusEnum.待确认, LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
        CanConfirm = true, Version = 1
    };

    private static ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>> Page(
        IEnumerable<PurchaseOrderManagerListItemDto> orders,
        int pageIndex,
        int pageSize)
    {
        var items = orders.ToList();
        return ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Ok(new()
        {
            Items = items, TotalCount = items.Count, PageIndex = pageIndex, PageSize = pageSize
        });
    }

    private static ProcurementConfirmationContextDto Context(
        PurchaseOrderManagerListItemDto order, int version = 2, string fingerprint = "fp", bool customerSupplied = false)
    {
        var context = new ProcurementConfirmationContextDto
        {
            PurchaseOrderId = order.PurchaseOrderId, Version = version, Fingerprint = fingerprint
        };
        if (customerSupplied)
        {
            context.Lines.Add(new ProcurementLineConfirmationContextDto
            {
                PurchaseOrderLineId = Guid.NewGuid(),
                LineVersion = 1
            });
        }
        return context;
    }

    private sealed record Fixture(PurchaseOrderManagerViewModel ViewModel, ManagerApiStub Manager, OrderApiStub Orders, DialogServiceStub Dialogs, ProcurementCommandSession Session);

    private sealed class ManagerApiStub(IEnumerable<PurchaseOrderManagerListItemDto> orders) : IPurchaseOrderManagerApi
    {
        private readonly PurchaseOrderManagerListItemDto[] _orders = orders.ToArray();
        public int SearchCount { get; private set; }
        public Func<PurchaseOrderManagerSearchRequest, Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>>? SearchHandler { get; set; }
        public Func<Guid, CancellationToken, Task<ServiceResult<List<PurchaseOrderManagerLineDto>>>>? LinesHandler { get; set; }
        public Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>> SearchAsync(PurchaseOrderManagerSearchRequest request, CancellationToken cancellationToken = default)
        {
            SearchCount++;
            return SearchHandler?.Invoke(request) ?? Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Ok(new()
            {
                Items = _orders.ToList(), TotalCount = _orders.Length, PageIndex = request.PageIndex, PageSize = request.PageSize
            }));
        }
        public Task<ServiceResult<List<PurchaseOrderManagerLineDto>>> GetLinesAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default)
            => LinesHandler?.Invoke(purchaseOrderId, cancellationToken) ?? Task.FromResult(ServiceResult<List<PurchaseOrderManagerLineDto>>.Ok([]));
    }

    /// <summary>批量确认协议替身；独立关联用例不由管理页承载，本测试拒绝调用这些入口。</summary>
    private sealed class OrderApiStub(IEnumerable<PurchaseOrderManagerListItemDto> orders) : IProcurementOrderApi
    {
        private readonly Dictionary<Guid, PurchaseOrderManagerListItemDto> _orders = orders.ToDictionary(static order => order.PurchaseOrderId);
        public List<(Guid OrderId, ConfirmProcurementOrderCommand Command)> ConfirmCalls { get; } = [];
        public Func<Guid, Task<ServiceResult<ProcurementConfirmationContextDto>>>? ContextHandler { get; set; }
        public Func<Guid, ConfirmProcurementOrderCommand, Task<ServiceResult<ProcurementOrderCommandResult>>>? ConfirmHandler { get; set; }
        public Func<Guid, Task<ServiceResult<PurchaseOrderDetailDto>>>? DetailHandler { get; set; }
        public Func<Guid, UpdatePurchaseOrderDraftCommand, Task<ServiceResult<ProcurementOrderCommandResult>>>? SaveHandler { get; set; }
        public List<UpdatePurchaseOrderDraftCommand> SaveCalls { get; } = [];
        public Task<ServiceResult<ProcurementConfirmationContextDto>> GetConfirmationContextAsync(Guid uid, CancellationToken cancellationToken = default)
            => ContextHandler?.Invoke(uid) ?? Task.FromResult(ServiceResult<ProcurementConfirmationContextDto>.Ok(Context(_orders[uid])));
        public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command, CancellationToken cancellationToken = default)
        {
            ConfirmCalls.Add((uid, command));
            return ConfirmHandler?.Invoke(uid, command) ?? Task.FromResult(ServiceResult<ProcurementOrderCommandResult>.Ok(Receipt(uid, command.ExpectedVersion)));
        }
        public Task<ServiceResult<PagedResponseBase<ProcurementOrderListItemDto>>> SearchAsync(ProcurementOrderSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<PurchaseOrderDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default) => DetailHandler?.Invoke(uid) ?? throw new NotSupportedException();
        public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command, CancellationToken cancellationToken = default)
        { SaveCalls.Add(command); return SaveHandler?.Invoke(uid, command) ?? throw new NotSupportedException(); }
        public Task<ServiceResult<ProcurementOrderCommandResult>> CancelConfirmationAsync(Guid uid, CancelProcurementOrderConfirmationCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewCancelConfirmationAsync(Guid uid, ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewVoidAsync(Guid uid, ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProcurementReversalPreviewDto>> PreviewDeleteLinesAsync(Guid uid, ProcurementReversalPreviewRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProcurementOrderCommandResult>> VoidAsync(Guid uid, VoidProcurementOrderCommand request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProcurementOrderCommandResult>> DeleteLinesAsync(Guid uid, DeleteProcurementDraftLinesCommand request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<ProcurementDemandAssociationContextDto>> GetDemandAssociationContextAsync(Guid uid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>> SearchDemandAssociationCandidatesAsync(Guid uid, ProcurementDemandCandidateSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<ProcurementDemandAssociationPreviewDto>> PreviewDemandAssociationAsync(Guid uid, PreviewProcurementDemandAssociationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        /// <inheritdoc/>
        public Task<ServiceResult<ProcurementOrderCommandResult>> AssociateDemandsAsync(Guid uid, AssociateProcurementDemandsCommand command, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class ApiCallRunnerStub : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public string LastConfirmationMessage { get; private set; } = string.Empty;
        public object? LastContent { get; private set; }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default)
        {
            LastContent = content;
            return Task.CompletedTask;
        }
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            LastConfirmationMessage = message;
            return Task.FromResult(ConfirmResult);
        }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class CurrentUserStub(IReadOnlyList<string> permissions) : ICurrentUserService
    {
        public event EventHandler? CurrentUserChanged { add { } remove { } }
        public bool IsAuthenticated => true;
        public MeDto? CurrentUser => new() { UserId = "u", Permissions = Permissions.ToList() };
        public string DisplayName => "测试用户";
        public IReadOnlyList<string> Roles => [];
        public IReadOnlyList<string> Permissions { get; } = permissions;
        public bool IsAdmin => false;
        public bool HasPermission(string permissionKey) => Permissions.Contains(permissionKey, StringComparer.Ordinal);
        public void SetCurrentUser(MeDto user) { }
        public void Clear() { }
    }

    private sealed class NavigationStub : INavigationService
    {
        public event PropertyChangedEventHandler? PropertyChanged { add { } remove { } }
        public ObservableCollection<NavigationTabItem> OpenTabs { get; } = [];
        public NavigationTabItem? SelectedTab { get; set; }
        public Task OpenAsync(string routeKey) => Task.CompletedTask;
        public Task OpenDocumentAsync(string routeKey, string documentKey, object? parameter = null, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void UpdateDocumentKey(object contentViewModel, string documentKey) { }
        public void Activate(NavigationTabItem? tab) { }
        public Task CloseAsync(NavigationTabItem? tab) => Task.CompletedTask;
        public void Reset() { }
    }

    private sealed class ProcurementDialogStub : IProcurementDialogService
    {
        public Task<PurchaseOrderFromMaterialResult> ShowFromMaterialAsync(CancellationToken cancellationToken = default) => Task.FromResult(new PurchaseOrderFromMaterialResult());
    }

    private sealed class MessageStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}

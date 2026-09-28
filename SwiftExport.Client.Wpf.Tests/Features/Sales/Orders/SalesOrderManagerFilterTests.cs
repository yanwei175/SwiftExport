using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Sales.Orders.Services;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderManagerFilterTests
{
    /// <summary>草稿删除按钮只接受独立 Delete 键，维护键不能替代。</summary>
    [TestMethod]
    public void DeleteDraftButton_UsesIndependentDeletePermission()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SalesOrderMaintain] });
        var model = new SalesOrderManagerViewModel(new RecordingManagerApi(), new RecordingDetailApi(),
            new DirectApiCallRunner(), null!, null!, null!, null!, user);
        model.SelectedOrder = new SalesOrderManagerListItemDto { Uid = Guid.NewGuid(), Version = 1,
            DocumentStatus = SalesOrderDocumentStatusEnum.Open,
            ConfirmationStatus = ConfirmationStatusEnum.待确认 };
        Assert.IsFalse(model.DeleteDraftCommand.CanExecute(null));

        user.SetCurrentUser(new MeDto { Permissions = [PermissionKeys.SalesOrderDelete] });
        Assert.IsTrue(model.DeleteDraftCommand.CanExecute(null));
    }

    /// <summary>查询 Busy 结束后必须再次通知列表绑定，允许继续选择和操作。</summary>
    [TestMethod]
    public async Task BrowseBindingRecoversAfterBusy()
    {
        var vm = new SalesOrderManagerViewModel(new RecordingManagerApi(), new RecordingDetailApi(),
            new DirectApiCallRunner(), null!, null!, null!, null!, AuthorizedUser());
        var states = new List<bool>();
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.CanBrowse)) states.Add(vm.CanBrowse);
        };
        await vm.SearchCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { false, true }, states);
        Assert.IsTrue(vm.CanBrowse);
    }

    /// <summary>为命令入口提供具备已分配操作权限的测试用户。</summary>
    private static CurrentUserService AuthorizedUser()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new MeDto { Permissions = PermissionCatalog.All.Select(item => item.PermissionKey).ToList() });
        return user;
    }
    /// <summary>
    /// 验证销售订单高级筛选使用按钮控制的全宽 Popup，并完整保留原有筛选字段。
    /// </summary>
    [TestMethod]
    public void AdvancedFilters_ShouldUseToggleControlledFullWidthPopup()
    {
        var repositoryRoot = FindRepositoryRoot();
        var managerView = XDocument.Load(Path.Combine(
            repositoryRoot,
            "SwiftExport.Client.Wpf",
            "Features",
            "Sales",
            "Orders",
            "Views",
            "SalesOrderManagerView.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

        Assert.IsFalse(managerView.Descendants(presentation + "Expander")
            .Any(element => string.Equals((string?)element.Attribute("Header"), "高级筛选", StringComparison.Ordinal)));
        var toggle = managerView.Descendants(presentation + "ToggleButton")
            .Single(element => string.Equals((string?)element.Attribute("Content"), "更多条件", StringComparison.Ordinal));
        StringAssert.Contains((string?)toggle.Attribute("IsChecked") ?? string.Empty, "IsAdvancedFilterOpen");

        var popup = managerView.Descendants(presentation + "Popup").Single();
        StringAssert.Contains((string?)popup.Attribute("IsOpen") ?? string.Empty, "IsAdvancedFilterOpen");
        StringAssert.Contains((string?)popup.Attribute("PlacementTarget") ?? string.Empty, "StackPanel");
        StringAssert.Contains((string?)popup.Attribute("Width") ?? string.Empty, "PlacementTarget.ActualWidth");
        Assert.AreEqual("True", (string?)popup.Attribute("StaysOpen"));
        Assert.AreEqual(4, popup.Descendants().Count(element => element.Name.LocalName == "TextBox"));
        // 订单日期和交期各有起止两个筛选值，保持四个输入的完整布局断言。
        Assert.AreEqual(4, popup.Descendants(presentation + "DatePicker").Count());
    }

    [TestMethod]
    public async Task InitializeAndSearch_ShouldLoadCustomerCodesAndSendAvailableStatusFilters()
    {
        var api = new RecordingManagerApi();
        var viewModel = new SalesOrderManagerViewModel(
            api,
            new RecordingDetailApi(),
            new DirectApiCallRunner(),
            null!,
            null!,
            null!,
            null!,
            AuthorizedUser());

        await viewModel.InitializeAsync();
        CollectionAssert.AreEqual(new[] { "C001", "C002" }, viewModel.CustomerCodeOptions);

        viewModel.CustomerCode = " C001 ";
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationStatusOptions[0]);
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationStatusOptions[1]);
        viewModel.SelectedDocumentStatuses.Add(viewModel.DocumentStatusOptions[1]);
        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSearchRequest);
        CollectionAssert.AreEqual(new[] { "C001" }, api.LastSearchRequest.CustomerCodes);
        CollectionAssert.AreEquivalent(
            new[] { ConfirmationStatusEnum.待确认, ConfirmationStatusEnum.已确认 },
            api.LastSearchRequest.ConfirmationStatuses);
        CollectionAssert.AreEqual(
            new[] { SalesOrderDocumentStatusEnum.Closed },
            api.LastSearchRequest.DocumentStatuses);
    }

    [TestMethod]
    public async Task ClearFilters_ShouldClearAllSelectedCollectionsAndCustomerCode()
    {
        var api = new RecordingManagerApi();
        var viewModel = new SalesOrderManagerViewModel(
            api,
            new RecordingDetailApi(),
            new DirectApiCallRunner(),
            null!,
            null!,
            null!,
            null!,
            AuthorizedUser());
        viewModel.CustomerCode = "MANUAL";
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationStatusOptions[0]);
        viewModel.SelectedDocumentStatuses.Add(viewModel.DocumentStatusOptions[0]);
        await viewModel.ClearFiltersCommand.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, viewModel.CustomerCode);
        Assert.IsEmpty(viewModel.SelectedConfirmationStatuses);
        Assert.IsEmpty(viewModel.SelectedDocumentStatuses);
        Assert.IsNotNull(api.LastSearchRequest);
        Assert.IsEmpty(api.LastSearchRequest.CustomerCodes);
    }

    /// <summary>
    /// 验证 PackingList 交付净额接入前，契约使用空值且页面明确显示待接入和未知。
    /// </summary>
    [TestMethod]
    public void DeliveryStatistics_ShouldRemainNullableAndDisplayUnknown()
    {
        Assert.AreEqual(
            typeof(decimal?),
            typeof(SalesOrderManagerListItemDto).GetProperty(nameof(SalesOrderManagerListItemDto.TotalShippedQty))!.PropertyType);
        Assert.AreEqual(
            typeof(decimal?),
            typeof(SalesOrderManagerListItemDto).GetProperty(nameof(SalesOrderManagerListItemDto.TotalUnshippedQty))!.PropertyType);
        Assert.AreEqual(
            typeof(SalesOrderFulfillmentStatusEnum?),
            typeof(SalesOrderManagerListItemDto).GetProperty(nameof(SalesOrderManagerListItemDto.FulfillmentStatus))!.PropertyType);
        Assert.AreEqual(
            typeof(decimal?),
            typeof(SalesOrderDetailLineDto).GetProperty(nameof(SalesOrderDetailLineDto.ShippedQty))!.PropertyType);
        Assert.AreEqual(
            typeof(decimal?),
            typeof(SalesOrderDetailLineDto).GetProperty(nameof(SalesOrderDetailLineDto.UnshippedQty))!.PropertyType);
        Assert.IsNull(typeof(SalesOrderManagerSearchRequest).GetProperty("FulfillmentStatuses"));

        var viewPath = Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Sales",
            "Orders",
            "Views",
            "SalesOrderManagerView.xaml");
        var viewSource = File.ReadAllText(viewPath);
        StringAssert.Contains(viewSource, "交付统计待接入");
        Assert.AreEqual(4, CountOccurrences(viewSource, "TargetNullValue=未知"));
        Assert.IsFalse(viewSource.Contains("Placeholder=\"执行状态\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task CancelConfirmation_WhenDialogIsClosed_ShouldNotCallApi()
    {
        var api = new RecordingDetailApi();
        var viewModel = CreateCancellationViewModel(api, new CancellationDialogStub(null));
        viewModel.SelectedOrder = ConfirmedOrder();

        await viewModel.CancelConfirmationCommand.ExecuteAsync(null);

        Assert.AreEqual(0, api.CancelCallCount);
    }

    [TestMethod]
    public async Task CancelConfirmation_ShouldSendReasonAndFreshCommandId()
    {
        var api = new RecordingDetailApi();
        var viewModel = CreateCancellationViewModel(api, new CancellationDialogStub("客户要求改单"));
        var order = ConfirmedOrder();
        viewModel.SelectedOrder = order;

        await viewModel.CancelConfirmationCommand.ExecuteAsync(null);

        Assert.AreEqual(1, api.CancelCallCount);
        Assert.IsNotNull(api.LastCancelCommand);
        Assert.AreEqual(order.Uid, api.LastCancelCommand.Uid);
        Assert.AreEqual(order.Version, api.LastCancelCommand.Version);
        Assert.AreEqual("客户要求改单", api.LastCancelCommand.Reason);
        Assert.AreNotEqual(Guid.Empty, api.LastCancelCommand.CommandId);
    }

    /// <summary>未知结果后限流不证明原命令未提交；切单后仍以原目标和版本重试。</summary>
    [TestMethod]
    public async Task Confirmation_UnknownThenRateLimited_RetriesOriginalOrderAndCommand()
    {
        var api = new RecordingDetailApi();
        api.ConfirmationFailures.Enqueue(ServiceErrorCodes.OutcomeUnknown);
        api.ConfirmationFailures.Enqueue(ServiceErrorCodes.RateLimited);
        var viewModel = new SalesOrderManagerViewModel(new RecordingManagerApi(), api,
            new DirectApiCallRunner(), null!, new ConfirmDialogStub(), null!, new MessageServiceStub(), AuthorizedUser());
        var first = new SalesOrderManagerListItemDto { Uid = Guid.NewGuid(), Version = 3,
            OrderNo = "SO-A", DocumentStatus = SalesOrderDocumentStatusEnum.Open,
            ConfirmationStatus = ConfirmationStatusEnum.待确认 };
        viewModel.SelectedOrder = first;
        await viewModel.ConfirmSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(1, api.ConfirmCommands.Count);
        Assert.IsFalse(await viewModel.CanCloseAsync(CancellationToken.None));
        viewModel.SelectedOrder = new SalesOrderManagerListItemDto { Uid = Guid.NewGuid(), Version = 4,
            OrderNo = "SO-B", DocumentStatus = SalesOrderDocumentStatusEnum.Open,
            ConfirmationStatus = ConfirmationStatusEnum.待确认 };
        await viewModel.RetryPendingConfirmationCommand.ExecuteAsync(null);
        Assert.AreEqual(2, api.ConfirmCommands.Count);
        Assert.AreEqual(first.Uid, api.ConfirmCommands[1].Uid);
        Assert.AreEqual(first.Version, api.ConfirmCommands[1].Version);
        Assert.AreEqual(api.ConfirmCommands[0].CommandId, api.ConfirmCommands[1].CommandId);
        Assert.IsFalse(await viewModel.CanCloseAsync(CancellationToken.None));
        Assert.IsTrue(viewModel.RetryPendingConfirmationCommand.CanExecute(null));
        await viewModel.RetryPendingConfirmationCommand.ExecuteAsync(null);
        Assert.AreEqual(3, api.ConfirmCommands.Count);
        Assert.AreSame(api.ConfirmCommands[0], api.ConfirmCommands[2]);
        Assert.AreEqual(first.Uid, api.ConfirmCommands[2].Uid);
        Assert.AreEqual(first.Version, api.ConfirmCommands[2].Version);
        Assert.IsTrue(await viewModel.CanCloseAsync(CancellationToken.None));
    }

    /// <summary>首次无权确认没有产生未知结果，不应阻止管理页关闭或残留重试入口。</summary>
    [TestMethod]
    public async Task InitialConfirmationForbiddenDoesNotLockPage()
    {
        var api = new RecordingDetailApi();
        api.ConfirmationFailures.Enqueue(ServiceErrorCodes.Forbidden);
        var vm = new SalesOrderManagerViewModel(new RecordingManagerApi(), api,
            new DirectApiCallRunner(), null!, new ConfirmDialogStub(), null!, new MessageServiceStub(), AuthorizedUser());
        vm.SelectedOrder = new SalesOrderManagerListItemDto { Uid = Guid.NewGuid(), Version = 3,
            DocumentStatus = SalesOrderDocumentStatusEnum.Open, ConfirmationStatus = ConfirmationStatusEnum.待确认 };
        await vm.ConfirmSelectedCommand.ExecuteAsync(null);
        Assert.IsTrue(await vm.CanCloseAsync(CancellationToken.None));
        Assert.IsFalse(vm.RetryPendingConfirmationCommand.CanExecute(null));
    }

    private static SalesOrderManagerViewModel CreateCancellationViewModel(
        RecordingDetailApi api,
        ISalesOrderCancellationDialogService cancellationDialog) =>
        new(
            new RecordingManagerApi(),
            api,
            new DirectApiCallRunner(),
            null!,
            null!,
            cancellationDialog,
            new MessageServiceStub(),
            AuthorizedUser());

    /// <summary>
    /// 从测试输出目录向上定位仓库根目录，用于读取销售订单页面 XAML 契约。
    /// </summary>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SwiftExport-Frontend.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    /// <summary>统计页面源码中稳定标记的出现次数。</summary>
    private static int CountOccurrences(string source, string marker)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(marker, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += marker.Length;
        }

        return count;
    }

    private static SalesOrderManagerListItemDto ConfirmedOrder() =>
        new()
        {
            Uid = Guid.NewGuid(),
            Version = 7,
            OrderNo = "SO-TEST",
            DocumentStatus = SalesOrderDocumentStatusEnum.Open,
            ConfirmationStatus = ConfirmationStatusEnum.已确认
        };

    /// <summary>记录 Manager 查询请求，验证列表页只通过 Manager API 加载筛选和分页数据。</summary>
    private sealed class RecordingManagerApi : ISalesOrderManagerApi
    {
        public SalesOrderManagerSearchRequest? LastSearchRequest { get; private set; }

        public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(
            SalesOrderManagerSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>.Ok(
                new PagedResponseBase<SalesOrderManagerListItemDto>
                {
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                }));
        }

        public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<List<string>>.Ok(["C001", "C002"]));
    }

    /// <summary>记录 Detail 快捷命令，验证 Manager 页复用 Detail API 的权威命令入口。</summary>
    private sealed class RecordingDetailApi : ISalesOrderDetailApi
    {
        public Queue<string> ConfirmationFailures { get; } = new();
        public List<ConfirmSalesOrderCommand> ConfirmCommands { get; } = [];
        public CancelSalesOrderConfirmationCommand? LastCancelCommand { get; private set; }
        public int CancelCallCount { get; private set; }

        public Task<ServiceResult<SalesOrderDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) => NotSupported<SalesOrderDetailDto>();
        public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default) => NotSupported<PagedResponseBase<SalesOrderProductLookupDto>>();
        public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default) => NotSupported<PagedResponseBase<SalesOrderCustomerLookupDto>>();
        public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request) => NotSupported<SaveSalesOrderResponse>();
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, ConfirmSalesOrderCommand request)
        {
            ConfirmCommands.Add(request);
            return Task.FromResult(ConfirmationFailures.TryDequeue(out var code)
                ? ServiceResult<SalesOrderConfirmationResultDto>.Fail(code, "本次请求未取得确认结果")
                : ServiceResult<SalesOrderConfirmationResultDto>.Ok(new SalesOrderConfirmationResultDto
                { Uid = uid, Version = request.Version + 1, GrossDemandCount = 1,
                    ConfirmationStatus = ConfirmationStatusEnum.已确认 }));
        }
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(
            Guid uid,
            CancelSalesOrderConfirmationCommand request)
        {
            CancelCallCount++;
            LastCancelCommand = request;
            return Task.FromResult(ServiceResult<SalesOrderConfirmationResultDto>.Ok(
                new SalesOrderConfirmationResultDto
                {
                    Uid = uid,
                    Version = request.Version + 1,
                    ConfirmationStatus = ConfirmationStatusEnum.待确认
                }));
        }
        public Task<ServiceResult<SalesOrderDetailDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request) => NotSupported<SalesOrderDetailDto>();
        public Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request) => NotSupported<bool>();

        private static Task<ServiceResult<T>> NotSupported<T>() =>
            Task.FromException<ServiceResult<T>>(new NotSupportedException());
    }

    private sealed class CancellationDialogStub(string? reason) : ISalesOrderCancellationDialogService
    {
        public Task<string?> RequestReasonAsync(
            string orderNo,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(reason);
    }

    private sealed class ConfirmDialogStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定",
            string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") =>
            ServiceResult<T>.Ok(await action(), successMessage);
    }
}

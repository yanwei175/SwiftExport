using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Sales.Orders.Services;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderManagerFilterTests
{
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
        Assert.AreEqual(2, popup.Descendants(presentation + "DatePicker").Count());
    }

    [TestMethod]
    public async Task InitializeAndSearch_ShouldLoadCustomerCodesAndSendMultiStatusFilters()
    {
        var api = new RecordingWorkspaceApi();
        var viewModel = new SalesOrderManagerViewModel(
            api,
            new DirectApiCallRunner(),
            null!,
            null!,
            null!,
            null!);

        await viewModel.InitializeAsync();
        CollectionAssert.AreEqual(new[] { "C001", "C002" }, viewModel.CustomerCodeOptions);

        viewModel.CustomerCode = " C001 ";
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationStatusOptions[0]);
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationStatusOptions[1]);
        viewModel.SelectedDocumentStatuses.Add(viewModel.DocumentStatusOptions[1]);
        viewModel.SelectedFulfillmentStatuses.Add(viewModel.FulfillmentStatusOptions[1]);
        viewModel.SelectedFulfillmentStatuses.Add(viewModel.FulfillmentStatusOptions[2]);

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSearchRequest);
        CollectionAssert.AreEqual(new[] { "C001" }, api.LastSearchRequest.CustomerCodes);
        CollectionAssert.AreEquivalent(
            new[] { ConfirmationStatusEnum.待确认, ConfirmationStatusEnum.已确认 },
            api.LastSearchRequest.ConfirmationStatuses);
        CollectionAssert.AreEqual(
            new[] { SalesOrderDocumentStatusEnum.Closed },
            api.LastSearchRequest.DocumentStatuses);
        CollectionAssert.AreEquivalent(
            new[] { SalesOrderFulfillmentStatusEnum.PartiallyShipped, SalesOrderFulfillmentStatusEnum.FullyShipped },
            api.LastSearchRequest.FulfillmentStatuses);
    }

    [TestMethod]
    public async Task ClearFilters_ShouldClearAllSelectedCollectionsAndCustomerCode()
    {
        var api = new RecordingWorkspaceApi();
        var viewModel = new SalesOrderManagerViewModel(
            api,
            new DirectApiCallRunner(),
            null!,
            null!,
            null!,
            null!);
        viewModel.CustomerCode = "MANUAL";
        viewModel.SelectedConfirmationStatuses.Add(viewModel.ConfirmationStatusOptions[0]);
        viewModel.SelectedDocumentStatuses.Add(viewModel.DocumentStatusOptions[0]);
        viewModel.SelectedFulfillmentStatuses.Add(viewModel.FulfillmentStatusOptions[0]);

        await viewModel.ClearFiltersCommand.ExecuteAsync(null);

        Assert.AreEqual(string.Empty, viewModel.CustomerCode);
        Assert.IsEmpty(viewModel.SelectedConfirmationStatuses);
        Assert.IsEmpty(viewModel.SelectedDocumentStatuses);
        Assert.IsEmpty(viewModel.SelectedFulfillmentStatuses);
        Assert.IsNotNull(api.LastSearchRequest);
        Assert.IsEmpty(api.LastSearchRequest.CustomerCodes);
    }

    [TestMethod]
    public async Task CancelConfirmation_WhenDialogIsClosed_ShouldNotCallApi()
    {
        var api = new RecordingWorkspaceApi();
        var viewModel = CreateCancellationViewModel(api, new CancellationDialogStub(null));
        viewModel.SelectedOrder = ConfirmedOrder();

        await viewModel.CancelConfirmationCommand.ExecuteAsync(null);

        Assert.AreEqual(0, api.CancelCallCount);
    }

    [TestMethod]
    public async Task CancelConfirmation_ShouldSendReasonAndFreshCommandId()
    {
        var api = new RecordingWorkspaceApi();
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

    private static SalesOrderManagerViewModel CreateCancellationViewModel(
        RecordingWorkspaceApi api,
        ISalesOrderCancellationDialogService cancellationDialog) =>
        new(
            api,
            new DirectApiCallRunner(),
            null!,
            null!,
            cancellationDialog,
            new MessageServiceStub());

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

    private static SalesOrderListItemDto ConfirmedOrder() =>
        new()
        {
            Uid = Guid.NewGuid(),
            Version = 7,
            OrderNo = "SO-TEST",
            DocumentStatus = SalesOrderDocumentStatusEnum.Open,
            ConfirmationStatus = ConfirmationStatusEnum.已确认
        };

    private sealed class RecordingWorkspaceApi : ISalesOrderWorkspaceApi
    {
        public SalesOrderSearchRequest? LastSearchRequest { get; private set; }
        public CancelSalesOrderConfirmationCommand? LastCancelCommand { get; private set; }
        public int CancelCallCount { get; private set; }

        public Task<ServiceResult<PagedResponseBase<SalesOrderListItemDto>>> SearchAsync(
            SalesOrderSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            LastSearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<SalesOrderListItemDto>>.Ok(
                new PagedResponseBase<SalesOrderListItemDto>
                {
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                }));
        }

        public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<List<string>>.Ok(["C001", "C002"]));

        public Task<ServiceResult<SalesOrderDocumentDto>> GetDocumentAsync(Guid uid, CancellationToken cancellationToken = default) => NotSupported<SalesOrderDocumentDto>();
        public Task<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> SearchProductsAsync(SalesOrderProductSearchRequest request, CancellationToken cancellationToken = default) => NotSupported<PagedResponseBase<SalesOrderProductLookupDto>>();
        public Task<ServiceResult<PagedResponseBase<SalesOrderCustomerLookupDto>>> SearchCustomersAsync(SalesOrderCustomerSearchRequest request, CancellationToken cancellationToken = default) => NotSupported<PagedResponseBase<SalesOrderCustomerLookupDto>>();
        public Task<ServiceResult<SaveSalesOrderResponse>> SaveAsync(SaveSalesOrderRequest request) => NotSupported<SaveSalesOrderResponse>();
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> ConfirmAsync(Guid uid, SalesOrderCommandRequest request) => NotSupported<SalesOrderConfirmationResultDto>();
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
        public Task<ServiceResult<SalesOrderDocumentDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request) => NotSupported<SalesOrderDocumentDto>();
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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.SalesOrders.Workspace;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Sales.Orders.ViewModels;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.Sales.Orders;

[TestClass]
public sealed class SalesOrderManagerFilterTests
{
    [TestMethod]
    public async Task InitializeAndSearch_ShouldLoadCustomerCodesAndSendMultiStatusFilters()
    {
        var api = new RecordingWorkspaceApi();
        var viewModel = new SalesOrderManagerViewModel(
            api,
            new DirectApiCallRunner(),
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

    private sealed class RecordingWorkspaceApi : ISalesOrderWorkspaceApi
    {
        public SalesOrderSearchRequest? LastSearchRequest { get; private set; }

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
        public Task<ServiceResult<SalesOrderConfirmationResultDto>> CancelConfirmationAsync(Guid uid, SalesOrderCommandRequest request) => NotSupported<SalesOrderConfirmationResultDto>();
        public Task<ServiceResult<SalesOrderDocumentDto>> CloseAsync(Guid uid, CloseSalesOrderRequest request) => NotSupported<SalesOrderDocumentDto>();
        public Task<ServiceResult<bool>> DeleteDraftAsync(Guid uid, SalesOrderCommandRequest request) => NotSupported<bool>();

        private static Task<ServiceResult<T>> NotSupported<T>() =>
            Task.FromException<ServiceResult<T>>(new NotSupportedException());
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") =>
            ServiceResult<T>.Ok(await action(), successMessage);
    }
}

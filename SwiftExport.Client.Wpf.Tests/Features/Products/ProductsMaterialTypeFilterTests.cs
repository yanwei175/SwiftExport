using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductsMaterialTypeFilterTests
{
    [TestMethod]
    public async Task DefaultWarehouseCandidates_UseProductApiWithoutInventoryCache()
    {
        var productApi = new RecordingProductApi();
        var viewModel = CreateViewModel(productApi);
        var load = typeof(ProductsViewModel).GetMethod("LoadProductWarehouseCandidatesAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        await (Task)load.Invoke(viewModel, null)!;

        Assert.AreEqual(1, productApi.WarehouseCandidateCalls);
        Assert.AreEqual("W-01", viewModel.WarehouseCandidates.Single().Code);
    }

    [TestMethod]
    public async Task Search_ShouldSendAllSelectedMaterialTypes()
    {
        var productApi = new RecordingProductApi();
        var viewModel = CreateViewModel(productApi);
        viewModel.FilterMaterialTypes.Add("成品");
        viewModel.FilterMaterialTypes.Add("客供件");

        await viewModel.SearchCommand.ExecuteAsync(null);

        Assert.IsNotNull(productApi.LastSearchRequest);
        CollectionAssert.AreEqual(
            new[] { "成品", "客供件" },
            productApi.LastSearchRequest.MaterialTypeList);
    }

    [TestMethod]
    public async Task SearchAndReset_ShouldSendEmptyMaterialTypeList()
    {
        var productApi = new RecordingProductApi();
        var viewModel = CreateViewModel(productApi);

        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.IsNotNull(productApi.LastSearchRequest);
        Assert.IsEmpty(productApi.LastSearchRequest.MaterialTypeList);

        viewModel.FilterMaterialTypes.Add("零件");
        await viewModel.ResetFiltersCommand.ExecuteAsync(null);

        Assert.IsEmpty(viewModel.FilterMaterialTypes);
        Assert.IsNotNull(productApi.LastSearchRequest);
        Assert.IsEmpty(productApi.LastSearchRequest.MaterialTypeList);
    }

    private static ProductsViewModel CreateViewModel(RecordingProductApi productApi)
    {
        var runner = new PassThroughApiCallRunner();
        var messageService = new NoOpMessageService();
        var dialogService = new NoOpDialogService();
        var bomDrawer = new BomDrawerViewModel(null!, productApi, runner, messageService, dialogService);

        return new ProductsViewModel(
            productApi,
            null!,
            null!,
            runner,
            null!,
            messageService,
            dialogService,
            bomDrawer,
            null!);
    }

    private sealed class RecordingProductApi : IProductApi
    {
        public ProductSearchRequest? LastSearchRequest { get; private set; }
        public int WarehouseCandidateCalls { get; private set; }

        public Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request)
        {
            LastSearchRequest = request;
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(
                new PagedResponseBase<ProductDto>
                {
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                    TotalCount = 0
                }));
        }

        public Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync() => NotSupported<List<ProductCategoryDto>>();

        public Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request) => NotSupported<List<ProductLookupDto>>();

        public Task<ServiceResult<List<string>>> GetAllCustomerCodeListAsync() => NotSupported<List<string>>();

        public Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku) => NotSupported<ProductDto>();

        public Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync(ProductBatchSaveRequest request) => NotSupported<ProductBatchSaveResponse>();
        public Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidatesAsync(
            PagedRequestBase request, CancellationToken cancellationToken = default)
        {
            WarehouseCandidateCalls++;
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Ok(
                new PagedResponseBase<ProductDefaultWarehouseCandidateDto>
                {
                    PageIndex = request.PageIndex, PageSize = request.PageSize, TotalCount = 1,
                    Items = [new ProductDefaultWarehouseCandidateDto { Uid = Guid.NewGuid(), Code = "W-01", Name = "主仓" }]
                }));
        }


        public Task<ServiceResult<ProductDto>> GetByIdAsync(Guid id) => NotSupported<ProductDto>();

        public Task<ServiceResult<Guid>> CreateAsync(ProductDto request) => NotSupported<Guid>();

        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductDto request) => NotSupported<Guid>();

        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();

        private static Task<ServiceResult<T>> NotSupported<T>() => throw new NotSupportedException();
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            string failureMessage = "请求失败") => action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    private sealed class NoOpMessageService : IMessageService
    {
        public void ShowInfo(string title, string message) { }

        public void ShowSuccess(string title, string message) { }

        public void ShowWarning(string title, string message) { }

        public void ShowError(string title, string message) { }
    }

    private sealed class NoOpDialogService : IDialogService
    {
        public Task ShowContentAsync(
            string title,
            object content,
            string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task ShowErrorAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

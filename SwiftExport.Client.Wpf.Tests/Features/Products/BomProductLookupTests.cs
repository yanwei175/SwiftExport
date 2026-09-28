using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class BomProductLookupTests
{
    [TestMethod]
    public async Task ConcurrentSearch_LatestResponseShouldWinAndExcludeParentProduct()
    {
        var firstResponse = new TaskCompletionSource<ServiceResult<List<ProductLookupDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var secondResponse = new TaskCompletionSource<ServiceResult<List<ProductLookupDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var requests = new List<ProductLookupRequest>();
        var api = new ProductApiStub(request =>
        {
            requests.Add(request);
            return request.Keyword == "first" ? firstResponse.Task : secondResponse.Task;
        });
        var parentId = Guid.NewGuid();
        var viewModel = CreateViewModel(api);
        viewModel.ActiveProduct = new ProductLineModel { Uid = parentId, Sku = "PARENT" };

        var firstSearch = viewModel.SearchProductsCommand.ExecuteAsync("first");
        var secondSearch = viewModel.SearchProductsCommand.ExecuteAsync("second");

        var newest = new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "SECOND" };
        secondResponse.SetResult(ServiceResult<List<ProductLookupDto>>.Ok([newest]));
        await secondSearch;
        firstResponse.SetResult(ServiceResult<List<ProductLookupDto>>.Ok(
            [new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "FIRST" }]));
        await firstSearch;

        Assert.HasCount(2, requests);
        Assert.IsTrue(requests.All(request => request.ExcludeProductId == parentId));
        Assert.IsTrue(requests.All(request => request.Limit == 20));
        Assert.HasCount(1, viewModel.ProductCandidates);
        Assert.AreSame(newest, viewModel.ProductCandidates[0]);
        Assert.IsFalse(viewModel.IsLookupBusy);
    }

    [TestMethod]
    public void CommitProduct_ShouldFillCompoundBomFieldsAndNormalizeQuantities()
    {
        var viewModel = CreateViewModel(new ProductApiStub());
        viewModel.ActiveProduct = new ProductLineModel { Uid = Guid.NewGuid(), Sku = "PARENT", BomNo = "BOM-1" };
        viewModel.SelectedCandidate = new ProductLookupDto
        {
            Uid = Guid.NewGuid(),
            Sku = "CHILD",
            DescriptionEn = string.Empty,
            DescriptionZh = "中文描述",
            Finish = "拉丝金",
            MainImage = @"C:\images\child.jpg",
        };
        var line = new BomLineModel { Numerator = 0, Denominator = -1 };

        viewModel.CommitProductCommand.Execute(line);

        Assert.AreEqual("BOM-1", line.BomNo);
        Assert.AreEqual(viewModel.SelectedCandidate.Uid, line.SubSkuId);
        Assert.AreEqual("CHILD", line.SubSku);
        Assert.AreEqual("中文描述", line.Description);
        Assert.AreEqual("拉丝金", line.Finish);
        Assert.AreEqual(@"C:\images\child.jpg", line.MainImage);
        Assert.AreEqual(1, line.Numerator);
        Assert.AreEqual(1, line.Denominator);
        Assert.AreSame(line, viewModel.SelectedLine);
    }

    [TestMethod]
    public async Task EmptyKeywordAndClosingDrawer_ShouldReleaseCandidates()
    {
        var viewModel = CreateViewModel(new ProductApiStub());
        viewModel.ProductCandidates.Add(new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "OLD" });
        viewModel.SelectedCandidate = viewModel.ProductCandidates[0];
        viewModel.IsLookupBusy = true;

        await viewModel.SearchProductsCommand.ExecuteAsync(string.Empty);

        Assert.IsEmpty(viewModel.ProductCandidates);
        Assert.IsNull(viewModel.SelectedCandidate);
        Assert.IsFalse(viewModel.IsLookupBusy);

        viewModel.ProductCandidates.Add(new ProductLookupDto { Uid = Guid.NewGuid(), Sku = "OLD-2" });
        viewModel.SelectedCandidate = viewModel.ProductCandidates[0];
        viewModel.IsOpen = true;
        await viewModel.CloseCommand.ExecuteAsync(null);

        Assert.IsEmpty(viewModel.ProductCandidates);
        Assert.IsNull(viewModel.SelectedCandidate);
        Assert.IsFalse(viewModel.IsOpen);
    }

    private static BomDrawerViewModel CreateViewModel(IProductApi productApi) =>
        new(null!, productApi, new DirectApiCallRunner(), null!, null!);

    private sealed class DirectApiCallRunner : IApiCallRunner
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
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class ProductApiStub(
        Func<ProductLookupRequest, Task<ServiceResult<List<ProductLookupDto>>>>? lookup = null) : IProductApi
    {
        public Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request) =>
            lookup?.Invoke(request)
            ?? Task.FromResult(ServiceResult<List<ProductLookupDto>>.Ok([]));

        public Task<ServiceResult<ProductDto>> GetByIdAsync(Guid id) => NotSupported<ProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(ProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync() => NotSupported<List<ProductCategoryDto>>();
        public Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request) => NotSupported<PagedResponseBase<ProductDto>>();
        public Task<ServiceResult<List<string>>> GetAllCustomerCodeListAsync() => NotSupported<List<string>>();
        public Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku) => NotSupported<ProductDto>();
        public Task<ServiceResult<ProductBatchSaveResponse>> BatchSaveAsync(ProductBatchSaveRequest request) => NotSupported<ProductBatchSaveResponse>();
        public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterDescriptionsAsync(PullProductMasterDataRequest request, CancellationToken cancellationToken = default) => NotSupported<PullProductMasterDataResponse>();
        public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterFeaturesAsync(PullProductMasterDataRequest request, CancellationToken cancellationToken = default) => NotSupported<PullProductMasterDataResponse>();
        public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterSupplierAsync(PullProductMasterDataRequest request, CancellationToken cancellationToken = default) => NotSupported<PullProductMasterDataResponse>();
        public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterWeightVolumeAsync(PullProductMasterDataRequest request, CancellationToken cancellationToken = default) => NotSupported<PullProductMasterDataResponse>();
        public Task<ServiceResult<PullProductMasterDataResponse>> PullMasterAllAsync(PullProductMasterDataRequest request, CancellationToken cancellationToken = default) => NotSupported<PullProductMasterDataResponse>();
        public Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidatesAsync(
            PagedRequestBase request, CancellationToken cancellationToken = default) => NotSupported<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>();

        private static Task<ServiceResult<T>> NotSupported<T>() =>
            Task.FromException<ServiceResult<T>>(new NotSupportedException());
    }
}

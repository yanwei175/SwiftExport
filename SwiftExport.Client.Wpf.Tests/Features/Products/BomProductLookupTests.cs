using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExportClient.Features.Products.Bom.Models;
using SwiftExportClient.Features.Products.Bom.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证物料抽屉父件资格及子件候选的请求隔离和字段接纳。</summary>
[TestClass]
public sealed class BomProductLookupTests
{
    /// <summary>非成品即使遗留 BOM 编号也不能打开组成编辑或发起读取。</summary>
    [TestMethod]
    [DataRow(MaterialTypesEnum.零件)] [DataRow(MaterialTypesEnum.虚拟件)]
    [DataRow(MaterialTypesEnum.客供件)] [DataRow(MaterialTypesEnum.包材)]
    public async Task Drawer_NonFinishedParentCannotOpen(string materialType)
    {
        var vm = CreateViewModel(new ProductApiStub());
        var product = new ProductLineModel { Uid = Guid.NewGuid(), Sku = "CHILD", BomNo = "LEGACY", MaterialType = materialType };
        vm.NotifyProductSelection(product);
        Assert.IsFalse(vm.ToggleCommand.CanExecute(product));
        await vm.ToggleCommand.ExecuteAsync(product);
        Assert.IsFalse(vm.IsOpen);
        Assert.IsEmpty(vm.Boms.Items);
    }

    /// <summary>成品的供货方式不影响抽屉入口；父件改为非成品后保留草稿但禁止继续提交。</summary>
    [TestMethod]
    [DataRow(SupplyTypeEnums.生产)] [DataRow(SupplyTypeEnums.采购)] [DataRow(SupplyTypeEnums.外协)]
    public async Task Drawer_FinishedParentTypeChangeBlocksWrites(string supplyType)
    {
        var api = BomManagerTests.Proxy<IBomApi>((method, _) => method.Name == "GetBomByBomNo"
            ? Task.FromResult(ServiceResult<List<BomDto>>.Ok([BomManagerTests.Line("ROOT")]))
            : throw new AssertFailedException("父件失去资格后不得写入：" + method.Name));
        var products = new ProductApiStub(); var runner = new DirectApiCallRunner();
        var vm = new BomDrawerViewModel(api, products, runner, null!, null!);
        using var manager = new ProductsViewModel(products, null!, null!, runner, null!, null!, null!, vm, null!);
        manager.Products.LoadFromDtos([new ProductDto { Uid = Guid.NewGuid(), Sku = "ROOT", BomNo = "ROOT", BomVersion = 1, MaterialType = MaterialTypesEnum.成品, SupplyType = supplyType }]);
        var parent = manager.Products.Items.Single(); manager.SelectedLine = parent;
        var toggleNotices = 0; vm.ToggleCommand.CanExecuteChanged += (_, _) => toggleNotices++;
        parent.MaterialType = MaterialTypesEnum.包材;
        Assert.IsFalse(vm.ToggleCommand.CanExecute(parent));
        Assert.IsTrue(toggleNotices > 0, "行内类型编辑必须通知打开入口。");
        parent.MaterialType = MaterialTypesEnum.成品;
        Assert.IsTrue(vm.ToggleCommand.CanExecute(parent));
        await vm.ToggleCommand.ExecuteAsync(parent);
        var line = vm.Boms.Items.Single(); line.Note = "保留草稿";
        Assert.IsTrue(vm.AddItemCommand.CanExecute(null));
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        var saveNotices = 0; vm.SaveCommand.CanExecuteChanged += (_, _) => saveNotices++;
        parent.MaterialType = MaterialTypesEnum.包材;
        Assert.IsTrue(saveNotices > 0, "已打开抽屉的保存按钮必须及时刷新。");
        Assert.IsFalse(vm.AddItemCommand.CanExecute(null));
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        vm.AddItemCommand.Execute(null);
        Assert.AreSame(line, vm.Boms.Items.Single());
        Assert.AreEqual("保留草稿", line.Note);
        Assert.IsTrue(vm.HasChanges);
    }

    /// <summary>抽屉打开后切到非成品时清除旧组成，不因遗留编号读取或开放编辑。</summary>
    [TestMethod]
    [DataRow(MaterialTypesEnum.零件)] [DataRow(MaterialTypesEnum.虚拟件)]
    [DataRow(MaterialTypesEnum.客供件)] [DataRow(MaterialTypesEnum.包材)]
    public async Task Drawer_SwitchToNonFinishedParentDoesNotLoadComponents(string materialType)
    {
        var api = BomManagerTests.Proxy<IBomApi>((method, args) => method.Name == "GetBomByBomNo" && (string)args![0]! == "ROOT"
            ? Task.FromResult(ServiceResult<List<BomDto>>.Ok([BomManagerTests.Line("ROOT")]))
            : throw new AssertFailedException("非成品不得读取或写入组成。"));
        var vm = new BomDrawerViewModel(api, new ProductApiStub(), new DirectApiCallRunner(), null!, null!);
        await vm.ToggleCommand.ExecuteAsync(new ProductLineModel { Uid = Guid.NewGuid(), MaterialType = MaterialTypesEnum.成品, BomVersion = 1, BomNo = "ROOT" });
        Assert.HasCount(1, vm.Boms.Items);
        var nonFinished = new ProductLineModel { Uid = Guid.NewGuid(), MaterialType = materialType, BomNo = "LEGACY" };
        vm.NotifyProductSelection(nonFinished);
        Assert.AreSame(nonFinished, vm.ActiveProduct);
        Assert.IsEmpty(vm.Boms.Items);
        Assert.IsFalse(vm.AddItemCommand.CanExecute(null));
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        Assert.IsTrue(vm.ToggleCommand.CanExecute(nonFinished), "失去父件资格后仍能关闭抽屉。");
    }

    /// <summary>加载等待期间类型或编号变化，旧响应不能把组成重新填到变化后的父件。</summary>
    [TestMethod] [DataRow(true)] [DataRow(false)]
    public async Task Drawer_ParentChangedDuringLoadRejectsOldResponse(bool changeMaterialType)
    {
        var pending = new TaskCompletionSource<ServiceResult<List<BomDto>>>();
        var api = BomManagerTests.Proxy<IBomApi>((method, _) => method.Name == "GetBomByBomNo" ? pending.Task
            : throw new AssertFailedException("不应写入。"));
        var vm = new BomDrawerViewModel(api, new ProductApiStub(), new DirectApiCallRunner(), null!, null!);
        var parent = new ProductLineModel { Uid = Guid.NewGuid(), MaterialType = MaterialTypesEnum.成品, BomVersion = 1, BomNo = "ROOT" };
        var loading = vm.ToggleCommand.ExecuteAsync(parent);
        if (changeMaterialType) parent.MaterialType = MaterialTypesEnum.零件;
        else parent.BomNo = "OTHER";
        pending.SetResult(ServiceResult<List<BomDto>>.Ok([BomManagerTests.Line("ROOT")]));
        await loading;
        Assert.IsEmpty(vm.Boms.Items);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
    }

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

    /// <summary>所选子件一次回填组成字段，父件为已打开的成品上下文。</summary>
    [TestMethod]
    public void CommitProduct_ShouldFillCompoundBomFieldsAndNormalizeQuantities()
    {
        var viewModel = CreateViewModel(new ProductApiStub());
        viewModel.IsOpen = true;
        viewModel.ActiveProduct = new ProductLineModel { Uid = Guid.NewGuid(), Sku = "PARENT", BomNo = "BOM-1", BomVersion = 1, MaterialType = MaterialTypesEnum.成品 };
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
        public Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request, CancellationToken cancellationToken = default) =>
            lookup?.Invoke(request)
            ?? Task.FromResult(ServiceResult<List<ProductLookupDto>>.Ok([]));

        public Task<ServiceResult<ProductDto>> GetByIdAsync(Guid id) => NotSupported<ProductDto>();
        /// <summary>此 BOM 查询夹具不提供物料详情读取。</summary>
        public Task<ServiceResult<ProductDetailDto>> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) => NotSupported<ProductDetailDto>();
        /// <summary>此 BOM 查询夹具不提供物料详情写入。</summary>
        public Task<ServiceResult<SaveProductDetailResponse>> SaveDetailAsync(Guid id, SaveProductDetailRequest request, CancellationToken cancellationToken = default) => NotSupported<SaveProductDetailResponse>();
        public Task<ServiceResult<Guid>> CreateAsync(ProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<List<ProductCategoryDto>>> GetCategoryTreeAsync() => NotSupported<List<ProductCategoryDto>>();
        public Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request, CancellationToken cancellationToken = default) => NotSupported<PagedResponseBase<ProductDto>>();
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

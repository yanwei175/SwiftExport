using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证共享分类筛选树在主型号和中性产品页面中的查询语义与取消恢复边界。</summary>
[TestClass]
public sealed class ProductCategoryFilterIntegrationTests
{
    /// <summary>两个管理页必须使用共享分类筛选控件，不再保留页面级 TreeView 选择桥接。</summary>
    [TestMethod]
    [DataRow("ProductMasterManagerView.xaml")]
    [DataRow("ProductNeutralManagerView.xaml")]
    public void Views_ShouldUseSharedCategoryFilterTree(string fileName)
    {
        var source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "SwiftExport.Client.Wpf",
            "Features",
            "Products",
            "ProductList",
            "Views",
            fileName));

        StringAssert.Contains(source, "<productComponents:ProductCategoryFilterTreeView");
        StringAssert.Contains(source, "Selection=\"{Binding CategoryFilter, Mode=TwoWay}\"");
        StringAssert.Contains(source, "SelectionChangedCommand=\"{Binding ApplyCategoryFilterCommand}\"");
        Assert.IsFalse(source.Contains("SelectedItemChanged=", StringComparison.Ordinal));
    }

    /// <summary>主型号默认不限制分类，组合筛选只发送有效且去重后的分类标识。</summary>
    [TestMethod]
    public async Task ProductMaster_ShouldSendAllAndCombinedCategoryFilters()
    {
        var api = new ProductMasterApiStub();
        using var viewModel = CreateProductMasterViewModel(api);

        await viewModel.SearchCommand.ExecuteAsync(null);

        var allRequest = api.Requests.Single();
        Assert.AreEqual(0, allRequest.CategoryIds.Count);
        Assert.IsFalse(allRequest.IncludeUncategorized);

        var firstCategoryId = Guid.NewGuid();
        var secondCategoryId = Guid.NewGuid();
        var selection = new ProductCategoryFilterSelection(
            false,
            true,
            [firstCategoryId, Guid.Empty, firstCategoryId, secondCategoryId]);
        viewModel.CategoryFilter = selection;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(selection);

        Assert.AreEqual(2, api.Requests.Count);
        var combinedRequest = api.Requests[1];
        CollectionAssert.AreEqual(
            new[] { firstCategoryId, secondCategoryId },
            combinedRequest.CategoryIds);
        Assert.IsTrue(combinedRequest.IncludeUncategorized);
        Assert.AreEqual(1, combinedRequest.PageIndex);
    }

    /// <summary>未确认放弃主型号编辑时恢复已应用选择，且不发出新的分页请求。</summary>
    [TestMethod]
    public async Task ProductMaster_ShouldRestoreAppliedFilterWhenDiscardIsCancelled()
    {
        var api = new ProductMasterApiStub();
        var dialogs = new DialogStub { Result = false };
        using var viewModel = CreateProductMasterViewModel(api, dialogs);
        viewModel.Masters.LoadFromDtos([new ProductMasterListItemDto
        {
            Uid = Guid.NewGuid(),
            Version = 1,
            Data = new ProductMasterWriteDto { MasterSku = "MASTER-1" }
        }]);
        viewModel.Masters.Items[0].ProductSeriesId = Guid.NewGuid();
        var requested = new ProductCategoryFilterSelection(false, false, [Guid.NewGuid()]);
        viewModel.CategoryFilter = requested;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(requested);

        Assert.AreSame(ProductCategoryFilterSelection.AllCategories, viewModel.CategoryFilter);
        Assert.AreEqual(0, api.Requests.Count);
        Assert.AreEqual(1, dialogs.Count);
    }

    /// <summary>中性产品分别传递“仅未分类”和“未分类加真实分类”的筛选条件。</summary>
    [TestMethod]
    public async Task ProductNeutral_ShouldSendUncategorizedAndCombinedCategoryFilters()
    {
        var api = new ProductNeutralApiStub();
        using var viewModel = new ProductNeutralManagerViewModel(
            api,
            null!,
            new Runner(),
            null!,
            new Messages(),
            new DialogStub(),
            new ImageAddressResolverStub());
        var uncategorized = new ProductCategoryFilterSelection(false, true, []);
        viewModel.CategoryFilter = uncategorized;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(uncategorized);

        Assert.AreEqual(1, api.Requests.Count);
        Assert.AreEqual(0, api.Requests[0].CategoryIds.Count);
        Assert.IsTrue(api.Requests[0].IncludeUncategorized);

        var categoryIds = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var combined = new ProductCategoryFilterSelection(false, true, categoryIds);
        viewModel.CategoryFilter = combined;

        await viewModel.ApplyCategoryFilterCommand.ExecuteAsync(combined);

        Assert.AreEqual(2, api.Requests.Count);
        CollectionAssert.AreEqual(categoryIds, api.Requests[1].CategoryIds);
        Assert.IsTrue(api.Requests[1].IncludeUncategorized);
    }

    private static ProductMasterManagerViewModel CreateProductMasterViewModel(
        IProductMasterApi api,
        IDialogService? dialogs = null) => new(
        api,
        new Runner(),
        null!,
        new Messages(),
        dialogs ?? new DialogStub(),
        null!,
        null!,
        new ImageAddressResolverStub(),
        null!);

    /// <summary>从测试运行目录向上定位仓库根目录。</summary>
    private static string FindRepositoryRoot()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))) return directory.FullName;
            }
        }

        Assert.Fail("无法定位 SwiftExport 仓库根目录。");
        return string.Empty;
    }

    /// <summary>记录主型号分页请求，并返回稳定空页。</summary>
    private sealed class ProductMasterApiStub : IProductMasterApi
    {
        public List<ProductMasterSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(
            ProductMasterSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new()));
        }

        public Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(
            AssignProductMastersToCategoryRequest request,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应执行主型号归类。");

        public Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应读取主型号详情。");

        public Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应保存主型号。");

        public Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应保存主型号详情。");
    }

    /// <summary>记录中性产品分页请求，并返回稳定空页。</summary>
    private sealed class ProductNeutralApiStub : IProductNeutralManagerApi
    {
        public List<ProductNeutralManagerSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
            ProductNeutralManagerSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>.Ok(new()));
        }

        public Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
            ProductNeutralManagerBatchSaveRequest request,
            CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类筛选不应保存中性产品。");
    }

    /// <summary>直接执行 API 委托，避免测试引入网络与 UI 错误转换。</summary>
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") => action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    /// <summary>记录未保存变更确认结果。</summary>
    private sealed class DialogStub : IDialogService
    {
        public bool Result { get; set; } = true;
        public int Count { get; private set; }

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default)
        {
            Count++;
            return Task.FromResult(Result);
        }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    /// <summary>隔离消息提示，测试只断言状态和请求。</summary>
    private sealed class Messages : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}

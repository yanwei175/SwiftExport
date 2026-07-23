using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Categories.Models;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
public sealed class ProductCategoryAssignmentViewModelTests
{
    [TestMethod]
    public async Task ProductQuery_ShouldBeLazyAndOnlyRunWhilePanelIsOpen()
    {
        var api = DispatchProxy.Create<IProductApi, ProductApiProxy>();
        var proxy = (ProductApiProxy)(object)api;
        proxy.PageResult = CreatePage();
        var viewModel = CreateViewModel(api);
        var category = Category("分类一");

        viewModel.SetHostState(category, hasCategoryChanges: false, canWrite: true);
        Assert.AreEqual(0, proxy.PageCallCount, "普通选择分类不应请求产品。");

        await viewModel.OpenAssignmentPanelCommand.ExecuteAsync(null);
        Assert.AreEqual(1, proxy.PageCallCount);
        Assert.IsTrue(viewModel.IsAssignmentPanelOpen);
        Assert.AreEqual(1, viewModel.Lines.Count);

        viewModel.Keyword = "SKU-001";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(2, proxy.PageCallCount);
        Assert.AreEqual("SKU-001", proxy.LastPageRequest?.Keyword);

        viewModel.CloseAssignmentPanelCommand.Execute(null);
        await viewModel.ChangeTargetAsync(Category("分类二"), hasCategoryChanges: false, canWrite: true);
        Assert.AreEqual(2, proxy.PageCallCount, "面板关闭后切换分类不应请求产品。");
        Assert.AreEqual(0, viewModel.Lines.Count, "关闭面板后应释放产品行。");
    }

    [TestMethod]
    public async Task AssignSelected_ShouldSendCurrentPageIdsAndVersions()
    {
        var api = DispatchProxy.Create<IProductApi, ProductApiProxy>();
        var proxy = (ProductApiProxy)(object)api;
        proxy.PageResult = CreatePage();
        proxy.AssignResult = ServiceResult<AssignProductsToCategoryResult>.Ok(
            new AssignProductsToCategoryResult { UpdatedCount = 1 });
        var viewModel = CreateViewModel(api);
        var category = Category("目标分类");
        viewModel.SetHostState(category, hasCategoryChanges: false, canWrite: true);
        await viewModel.OpenAssignmentPanelCommand.ExecuteAsync(null);

        viewModel.Lines[0].IsChecked = true;
        Assert.IsTrue(viewModel.AssignSelectedCommand.CanExecute(null));
        await viewModel.AssignSelectedCommand.ExecuteAsync(null);

        Assert.IsNotNull(proxy.AssignRequest);
        Assert.AreEqual(category.Uid, proxy.AssignRequest.TargetCategoryId);
        Assert.AreEqual(viewModel.Lines[0].Uid, proxy.AssignRequest.Items.Single().ProductId);
        Assert.AreEqual(7, proxy.AssignRequest.Items.Single().ExpectedVersion);
    }

    [TestMethod]
    public async Task UnsavedCategoryChanges_ShouldDisableAssignmentButStillAllowReading()
    {
        var api = DispatchProxy.Create<IProductApi, ProductApiProxy>();
        var proxy = (ProductApiProxy)(object)api;
        proxy.PageResult = CreatePage();
        var viewModel = CreateViewModel(api);
        var category = Category("待修改分类");
        viewModel.SetHostState(category, hasCategoryChanges: true, canWrite: true);
        await viewModel.OpenAssignmentPanelCommand.ExecuteAsync(null);
        viewModel.Lines[0].IsChecked = true;

        Assert.AreEqual(1, proxy.PageCallCount);
        Assert.IsFalse(viewModel.AssignSelectedCommand.CanExecute(null));
    }

    private static ProductCategoryAssignmentViewModel CreateViewModel(IProductApi api) => new(
        api,
        new DirectApiCallRunner(),
        new MessageServiceStub(),
        new DialogServiceStub());

    private static ProductCategoryTreeItemModel Category(string name) => new()
    {
        Uid = Guid.NewGuid(),
        Version = 1,
        CategoryName = name,
        State = ProductCategoryNodeState.Unchanged
    };

    private static ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>> CreatePage() =>
        ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>.Ok(
            new PagedResponseBase<ProductCategoryAssignmentRowDto>
            {
                PageIndex = 1,
                PageSize = 50,
                TotalCount = 1,
                Items =
                [
                    new ProductCategoryAssignmentRowDto
                    {
                        Uid = Guid.NewGuid(),
                        Version = 7,
                        Sku = "SKU-001",
                        OriginalCategoryName = "原分类"
                    }
                ]
            });

    private class ProductApiProxy : DispatchProxy
    {
        public int PageCallCount { get; private set; }
        public ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>> PageResult { get; set; } =
            ServiceResult<PagedResponseBase<ProductCategoryAssignmentRowDto>>.Ok(new PagedResponseBase<ProductCategoryAssignmentRowDto>());
        public ServiceResult<AssignProductsToCategoryResult> AssignResult { get; set; } =
            ServiceResult<AssignProductsToCategoryResult>.Ok(new AssignProductsToCategoryResult());
        public AssignProductsToCategoryRequest? AssignRequest { get; private set; }
        public ProductCategoryAssignmentSearchRequest? LastPageRequest { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IProductApi.GetCategoryAssignmentPageAsync))
            {
                PageCallCount++;
                LastPageRequest = (ProductCategoryAssignmentSearchRequest)args![0]!;
                return Task.FromResult(PageResult);
            }

            if (targetMethod?.Name == nameof(IProductApi.AssignCategoryAsync))
            {
                AssignRequest = (AssignProductsToCategoryRequest)args![0]!;
                return Task.FromResult(AssignResult);
            }

            throw new AssertFailedException($"未配置产品 API：{targetMethod?.Name}");
        }
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") =>
            ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) => Assert.Fail($"{title}: {message}");
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

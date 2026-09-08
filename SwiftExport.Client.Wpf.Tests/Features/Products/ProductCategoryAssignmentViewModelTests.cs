using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
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
    public async Task ProductMasterQuery_ShouldBeLazyAndOnlyRunWhilePanelIsOpen()
    {
        var api = DispatchProxy.Create<IProductMasterApi, ProductMasterApiProxy>();
        var proxy = (ProductMasterApiProxy)(object)api;
        proxy.PageResult = CreatePage();
        var viewModel = CreateViewModel(api);
        var category = Category("分类一");

        viewModel.SetHostState(category, hasCategoryChanges: false, canWrite: true);
        Assert.AreEqual(0, proxy.PageCallCount, "普通选择分类不应请求主型号。");

        await viewModel.OpenAssignmentPanelCommand.ExecuteAsync(null);
        Assert.AreEqual(1, proxy.PageCallCount);
        Assert.IsTrue(viewModel.IsAssignmentPanelOpen);
        Assert.AreEqual(1, viewModel.Lines.Count);
        Assert.AreEqual(category.Uid, proxy.LastPageRequest?.ExcludeCategoryId);

        viewModel.Keyword = "SKU-001";
        await viewModel.SearchCommand.ExecuteAsync(null);
        Assert.AreEqual(2, proxy.PageCallCount);
        Assert.AreEqual("SKU-001", proxy.LastPageRequest?.Keyword);

        viewModel.CloseAssignmentPanelCommand.Execute(null);
        await viewModel.ChangeTargetAsync(Category("分类二"), hasCategoryChanges: false, canWrite: true);
        Assert.AreEqual(2, proxy.PageCallCount, "面板关闭后切换分类不应请求主型号。");
        Assert.AreEqual(0, viewModel.Lines.Count, "关闭面板后应释放主型号行。");
    }

    [TestMethod]
    public async Task AssignSelected_ShouldSendCurrentPageIdsAndVersions()
    {
        var api = DispatchProxy.Create<IProductMasterApi, ProductMasterApiProxy>();
        var proxy = (ProductMasterApiProxy)(object)api;
        proxy.PageResult = CreatePage();
        proxy.AssignResult = ServiceResult<AssignProductMastersToCategoryResult>.Ok(
            new AssignProductMastersToCategoryResult { UpdatedCount = 1 });
        var viewModel = CreateViewModel(api);
        var category = Category("目标分类");
        viewModel.SetHostState(category, hasCategoryChanges: false, canWrite: true);
        await viewModel.OpenAssignmentPanelCommand.ExecuteAsync(null);

        viewModel.Lines[0].IsChecked = true;
        Assert.IsTrue(viewModel.AssignSelectedCommand.CanExecute(null));
        await viewModel.AssignSelectedCommand.ExecuteAsync(null);

        Assert.IsNotNull(proxy.AssignRequest);
        Assert.AreEqual(category.Uid, proxy.AssignRequest.TargetCategoryId);
        Assert.AreEqual(viewModel.Lines[0].Uid, proxy.AssignRequest.Items.Single().ProductMasterId);
        Assert.AreEqual(7, proxy.AssignRequest.Items.Single().ExpectedVersion);
    }

    [TestMethod]
    public async Task UnsavedCategoryChanges_ShouldDisableAssignmentButStillAllowReading()
    {
        var api = DispatchProxy.Create<IProductMasterApi, ProductMasterApiProxy>();
        var proxy = (ProductMasterApiProxy)(object)api;
        proxy.PageResult = CreatePage();
        var viewModel = CreateViewModel(api);
        var category = Category("待修改分类");
        viewModel.SetHostState(category, hasCategoryChanges: true, canWrite: true);
        await viewModel.OpenAssignmentPanelCommand.ExecuteAsync(null);
        viewModel.Lines[0].IsChecked = true;

        Assert.AreEqual(1, proxy.PageCallCount);
        Assert.IsFalse(viewModel.AssignSelectedCommand.CanExecute(null));
    }

    private static ProductCategoryAssignmentViewModel CreateViewModel(IProductMasterApi api) => new(
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

    private static ServiceResult<PagedResponseBase<ProductMasterListItemDto>> CreatePage() =>
        ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(
            new PagedResponseBase<ProductMasterListItemDto>
            {
                PageIndex = 1,
                PageSize = 50,
                TotalCount = 1,
                Items =
                [
                    new ProductMasterListItemDto
                    {
                        Uid = Guid.NewGuid(),
                        Version = 7,
                        CategoryName = "原分类",
                        Data = new ProductMasterWriteDto
                        {
                            MasterSku = "MASTER-001",
                            DefaultDescriptionZh = "主型号描述"
                        }
                    }
                ]
            });

    private class ProductMasterApiProxy : DispatchProxy
    {
        public int PageCallCount { get; private set; }
        public ServiceResult<PagedResponseBase<ProductMasterListItemDto>> PageResult { get; set; } =
            ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new PagedResponseBase<ProductMasterListItemDto>());
        public ServiceResult<AssignProductMastersToCategoryResult> AssignResult { get; set; } =
            ServiceResult<AssignProductMastersToCategoryResult>.Ok(new AssignProductMastersToCategoryResult());
        public AssignProductMastersToCategoryRequest? AssignRequest { get; private set; }
        public ProductMasterSearchRequest? LastPageRequest { get; private set; }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == nameof(IProductMasterApi.GetPagedAsync))
            {
                PageCallCount++;
                LastPageRequest = (ProductMasterSearchRequest)args![0]!;
                return Task.FromResult(PageResult);
            }

            if (targetMethod?.Name == nameof(IProductMasterApi.AssignCategoryAsync))
            {
                AssignRequest = (AssignProductMastersToCategoryRequest)args![0]!;
                return Task.FromResult(AssignResult);
            }

            throw new AssertFailedException($"未配置主型号 API：{targetMethod?.Name}");
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

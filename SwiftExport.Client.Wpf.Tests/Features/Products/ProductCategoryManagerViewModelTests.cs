using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Categories.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证分类保存回执接纳和移出标签定义后的原生表单保护。</summary>
[TestClass]
public sealed class ProductCategoryManagerViewModelTests
{
    [TestMethod]
    public async Task Save_ShouldReloadServerVersionInsideWriteBusyScope()
    {
        var categoryApi = new ProductCategoryApiStub();
        var runner = new DirectApiCallRunner();
        using var cache = new ReferenceDataCacheService(
            DispatchProxy.Create<ICustomerApi, NoCallApiProxy>(),
            DispatchProxy.Create<ISupplierApi, NoCallApiProxy>(),
            DispatchProxy.Create<IWarehouseSiteApi, NoCallApiProxy>(),
            new ProductColorApiStub(),
            categoryApi,
            new ProductAttributeApiStub(),
            DispatchProxy.Create<ISysDicCurrencyApi, NoCallApiProxy>(),
            DispatchProxy.Create<ISysDictTypeApi, NoCallApiProxy>(),
            DispatchProxy.Create<ISysDictDataApi, NoCallApiProxy>(),
            runner,
            new SessionServiceStub());
        var viewModel = new ProductCategoryManagerViewModel(
            categoryApi,
            runner,
            new MessageServiceStub(),
            new DialogServiceStub(),
            cache,
            new ProductCategoryAssignmentViewModel(
                DispatchProxy.Create<IProductMasterApi, NoCallProductApiProxy>(),
                runner,
                new MessageServiceStub(),
                new DialogServiceStub()));

        await viewModel.InitializeAsync();
        viewModel.CurrentCategory!.CategoryName = "第一次修改";
        await viewModel.SaveCommand.ExecuteAsync(null);

        Assert.AreEqual(2, viewModel.CurrentCategory!.Version);
        Assert.AreEqual("第一次修改", viewModel.CurrentCategory.CategoryName);

        viewModel.CurrentCategory.CategoryName = "第二次修改";
        await viewModel.SaveCommand.ExecuteAsync(null);

        CollectionAssert.AreEqual(new[] { 1, 2 }, categoryApi.SubmittedVersions.ToArray());
        Assert.AreEqual(3, viewModel.CurrentCategory!.Version);
        Assert.AreEqual("第二次修改", viewModel.CurrentCategory.CategoryName);
        Assert.IsTrue(categoryApi.GetAllCallCount >= 3, "保存后应绕过嵌套 Busy 限制并重新读取服务端模型。");
    }

    /// <summary>无效排序输入尚未进入模型时也必须阻止关闭，修正后正常形成分类草稿。</summary>
    [TestMethod]
    public void InvalidCategoryFormInput_BlocksCloseBeforeModelChange() => SwiftExport.Client.Wpf.Tests.Controls.DatePickerAssistTests.RunWithApplication(async () =>
    {
        SwiftExport.Client.Wpf.Tests.TestInfrastructure.ApplicationResourceLoader.Load();
        var runner = new DirectApiCallRunner();
        using var viewModel = new ProductCategoryManagerViewModel(new ProductCategoryApiStub(), runner,
            new MessageServiceStub(), new DialogServiceStub(), new SwiftExport.Client.Wpf.Tests.TestInfrastructure.ReferenceDataCacheServiceStub(),
            new ProductCategoryAssignmentViewModel(DispatchProxy.Create<IProductMasterApi, NoCallProductApiProxy>(),
                runner, new MessageServiceStub(), new DialogServiceStub()));
        await viewModel.InitializeAsync();
        var view = new SwiftExportClient.Features.Products.Categories.Views.ProductCategoryManagerView { DataContext = viewModel };
        var window = new System.Windows.Window { Content = view, Width = 1240, Height = 820, ShowInTaskbar = false };
        try
        {
            window.Show(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            var input = (System.Windows.Controls.TextBox)view.FindName("CategorySortInput");
            input.Text = "非法排序"; input.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(System.Windows.Controls.Validation.GetHasError(input));
            Assert.IsFalse(viewModel.HasUnsavedChanges, "转换错误不能伪造已写入模型的值。");
            Assert.IsFalse(await viewModel.CanCloseAsync(default));
            input.Text = "15"; input.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(view.CommitPendingEdits());
            Assert.AreEqual(15, viewModel.CurrentCategory!.SortNo);
            Assert.IsTrue(viewModel.HasUnsavedChanges);
        }
        finally { window.Close(); await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle); }
        Assert.IsNull(viewModel.CommitPendingEdits);
    });

    private sealed class ProductCategoryApiStub : IProductCategoryApi
    {
        private readonly Guid _uid = Guid.NewGuid();
        private int _version = 1;
        private string _name = "初始分类";

        public int GetAllCallCount { get; private set; }
        public List<int> SubmittedVersions { get; } = [];

        public Task<ServiceResult<List<ProductCategoryDto>>> GetAllAsync()
        {
            GetAllCallCount++;
            return Task.FromResult(ServiceResult<List<ProductCategoryDto>>.Ok(
            [
                new ProductCategoryDto
                {
                    Uid = _uid,
                    Version = _version,
                    CategoryName = _name,
                    CategoryCode = "ROOT",
                    SortNo = 10
                }
            ]));
        }

        public Task<ServiceResult<ProductCategoryBatchSaveResponse>> BatchSaveAsync(
            ProductCategoryBatchSaveRequest request)
        {
            var update = request.UpdateItems.Single();
            SubmittedVersions.Add(update.Version);
            Assert.AreEqual(_version, update.Version, "保存请求必须使用最近一次服务端 Version。");
            _name = update.Data.CategoryName;
            _version++;
            return Task.FromResult(ServiceResult<ProductCategoryBatchSaveResponse>.Ok(
                new ProductCategoryBatchSaveResponse
                {
                    TotalCount = 1,
                    Items =
                    [
                        new SaveItemResult
                        {
                            Uid = _uid,
                            Success = true,
                            OldVersion = update.Version,
                            NewVersion = _version
                        }
                    ]
                }));
        }

        public Task<ServiceResult<PagedResponseBase<ProductCategoryDto>>> GetPagedAsync(ProductCategorySearchRequest request) =>
            throw new NotSupportedException();
        public Task<ServiceResult<ProductCategoryDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductCategoryDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class ProductColorApiStub : IProductColorApi
    {
        public Task<ServiceResult<List<ProductColorDto>>> GetAllAsync() =>
            Task.FromResult(ServiceResult<List<ProductColorDto>>.Ok([]));
        public Task<ServiceResult<PagedResponseBase<ProductColorDto>>> GetPagedAsync(ProductColorSearchRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductColorBatchSaveResponse>> BatchSaveAsync(ProductColorBatchSaveRequest request) => throw new NotSupportedException();
        public Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid id) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> CreateAsync(ProductColorDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, ProductColorDto request) => throw new NotSupportedException();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => throw new NotSupportedException();
    }

    /// <summary>本回归只验证分类保存，不访问真实标签定义服务。</summary>
    private sealed class ProductAttributeApiStub : IProductAttributeApi
    {
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(new ProductAttributeDefinitionsDto()));
        public Task<ServiceResult<ProductAttributeDefinitionsDto>> GetCategoryDefinitionsAsync(Guid categoryId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(new ProductAttributeDefinitionsDto { CategoryId = categoryId }));
        /// <summary>分类页不发起标签维护写入。</summary>
        public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default) =>
            throw new AssertFailedException("分类保存不应提交标签定义。");
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

    private sealed class SessionServiceStub : ISessionService
    {
        public event EventHandler<SessionEndedEventArgs>? SessionEnded;
        public void ClearSession() { }
        public void EndSession(SessionEndReason reason) => SessionEnded?.Invoke(this, new SessionEndedEventArgs(reason));
    }

    private class NoCallProductApiProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new AssertFailedException($"不应调用产品 API：{targetMethod?.Name}");
    }

    private class NoCallApiProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new AssertFailedException($"不应调用系统字典 API：{targetMethod?.Name}");
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

[TestClass]
public sealed class SupplierProductWorkspaceTests
{
    [TestMethod]
    public async Task SelectSupplier_ShouldSearchFirstPageAndOpenEditorForPersistedSupplier()
    {
        var supplierId = Guid.NewGuid();
        var supplierApi = new SupplierApiStub(
        [
            new SupplierDto
            {
                Uid = supplierId,
                Version = 1,
                SupplierCode = "S001",
                SupplierName = "供应商一",
                DocRelateNo = "DOC-S001",
            },
        ]);
        var supplierProductApi = new SupplierProductApiStub();
        using var workspace = CreateWorkspace(
            supplierApi,
            supplierProductApi,
            new DialogServiceStub());

        Assert.IsFalse(workspace.Editor.IsOpen);
        Assert.IsEmpty(supplierProductApi.Requests);

        await workspace.SearchSuppliersCommand.ExecuteAsync("S00");

        Assert.HasCount(1, supplierApi.Requests);
        Assert.AreEqual("S00", supplierApi.Requests[0].Keyword);
        Assert.AreEqual(1, supplierApi.Requests[0].PageIndex);
        Assert.AreEqual(20, supplierApi.Requests[0].PageSize);
        Assert.HasCount(1, workspace.SupplierCandidates);

        workspace.SelectedSupplier = workspace.SupplierCandidates[0];
        await workspace.CommitSupplierCommand.ExecuteAsync(null);

        Assert.IsTrue(workspace.Editor.IsOpen);
        Assert.AreEqual(supplierId, workspace.Editor.ActiveSupplier?.Uid);
        Assert.HasCount(1, supplierProductApi.Requests);
        Assert.AreEqual(supplierId, supplierProductApi.Requests[0].SupplierId);
        Assert.AreEqual("供应商产品管理 - S001", workspace.DocumentTitle);
    }

    [TestMethod]
    public async Task SearchSuppliers_WhenOlderResponseCompletesLast_ShouldKeepNewestCandidates()
    {
        var oldResponse = new TaskCompletionSource<ServiceResult<PagedResponseBase<SupplierDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var supplierApi = new SupplierApiStub([], oldResponse);
        using var workspace = CreateWorkspace(
            supplierApi,
            new SupplierProductApiStub(),
            new DialogServiceStub());

        var olderSearch = workspace.SearchSuppliersCommand.ExecuteAsync("OLD");
        await workspace.SearchSuppliersCommand.ExecuteAsync("NEW");
        oldResponse.SetResult(SupplierApiStub.Result(
            new SupplierDto
            {
                Uid = Guid.NewGuid(),
                Version = 1,
                SupplierCode = "OLD",
                SupplierName = "旧供应商",
                DocRelateNo = "DOC-OLD",
            }));
        await olderSearch;

        Assert.HasCount(1, workspace.SupplierCandidates);
        Assert.AreEqual("NEW", workspace.SupplierCandidates[0].SupplierCode);
    }

    [TestMethod]
    public async Task SwitchOrCloseWithDirtyEditor_WhenDiscardRejected_ShouldRestoreActiveSupplier()
    {
        var dialog = new DialogServiceStub { ConfirmationResult = false };
        var supplierProductApi = new SupplierProductApiStub();
        using var workspace = CreateWorkspace(
            new SupplierApiStub([]),
            supplierProductApi,
            dialog);
        var first = Supplier(Guid.NewGuid(), "S001", "供应商一");
        var second = Supplier(Guid.NewGuid(), "S002", "供应商二");

        workspace.SelectedSupplier = first;
        await workspace.CommitSupplierCommand.ExecuteAsync(null);
        workspace.Editor.Products.AddNewItem(
            first.Uid,
            first.SupplierCode,
            first.SupplierName,
            first.Currency,
            first.TaxRate);
        workspace.SelectedSupplier = second;

        await workspace.CommitSupplierCommand.ExecuteAsync(null);

        Assert.AreSame(first, workspace.SelectedSupplier);
        Assert.AreSame(first, workspace.Editor.ActiveSupplier);
        Assert.HasCount(1, supplierProductApi.Requests);
        Assert.IsFalse(await workspace.CanCloseAsync(CancellationToken.None));
        Assert.IsTrue(workspace.HasUnsavedChanges);
    }

    private static SupplierProductWorkspaceViewModel CreateWorkspace(
        ISupplierApi supplierApi,
        ISupplierProductApi supplierProductApi,
        IDialogService dialogService)
    {
        var runner = new DirectApiCallRunner();
        var editor = new SupplierProductManagerViewModel(
            supplierProductApi,
            null!,
            runner,
            new MessageServiceStub(),
            dialogService);
        return new SupplierProductWorkspaceViewModel(supplierApi, runner, editor);
    }

    private static SupplierLineModel Supplier(Guid uid, string code, string name)
    {
        var supplier = new SupplierLineModel();
        supplier.Load(new SupplierDto
        {
            Uid = uid,
            Version = 1,
            SupplierCode = code,
            SupplierName = name,
            DocRelateNo = $"DOC-{code}",
            Currency = "CNY",
        });
        return supplier;
    }

    private sealed class SupplierApiStub(
        IReadOnlyList<SupplierDto> suppliers,
        TaskCompletionSource<ServiceResult<PagedResponseBase<SupplierDto>>>? oldResponse = null)
        : ISupplierApi
    {
        public List<SupplierSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync(SupplierSearchRequest request)
        {
            Requests.Add(request);
            if (string.Equals(request.Keyword, "OLD", StringComparison.Ordinal))
            {
                return oldResponse!.Task;
            }

            if (string.Equals(request.Keyword, "NEW", StringComparison.Ordinal))
            {
                return Task.FromResult(Result(new SupplierDto
                {
                    Uid = Guid.NewGuid(),
                    Version = 1,
                    SupplierCode = "NEW",
                    SupplierName = "新供应商",
                    DocRelateNo = "DOC-NEW",
                }));
            }

            return Task.FromResult(Result(suppliers.ToArray()));
        }

        public static ServiceResult<PagedResponseBase<SupplierDto>> Result(params SupplierDto[] suppliers) =>
            ServiceResult<PagedResponseBase<SupplierDto>>.Ok(new PagedResponseBase<SupplierDto>
            {
                Items = suppliers.ToList(),
                TotalCount = suppliers.Length,
                PageIndex = 1,
                PageSize = 20,
            });

        public Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest request) =>
            NotSupported<SupplierBatchSaveResponse>();
        public Task<ServiceResult<SupplierDto>> GetByIdAsync(Guid id) => NotSupported<SupplierDto>();
        public Task<ServiceResult<Guid>> CreateAsync(SupplierDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SupplierDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
    }

    private sealed class SupplierProductApiStub : ISupplierProductApi
    {
        public List<SupplierProductSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync(
            SupplierProductSearchRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(
                new PagedResponseBase<SupplierProductDto>
                {
                    Items = [],
                    TotalCount = 0,
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                }));
        }

        public Task<ServiceResult<SupplierProductDto>> GetByIdAsync(Guid id) =>
            NotSupported<SupplierProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(SupplierProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SupplierProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync(
            SupplierProductBatchSaveRequest request) =>
            NotSupported<SupplierProductBatchSaveResponse>();
        public Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(
            SetProductDefaultSupplierCommand command) =>
            NotSupported<SetProductDefaultSupplierResult>();
    }

    private sealed class DirectApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            string failureMessage = "请求失败") =>
            action();

        public Task<ServiceResult<T>> RunAsync<T>(
            Func<Task<ServiceResult<T>>> action,
            CancellationToken cancellationToken,
            string failureMessage = "请求失败") =>
            action();

        public async Task<ServiceResult<T>> RunValueAsync<T>(
            Func<Task<T>> action,
            string successMessage = "成功",
            string failureMessage = "请求失败") =>
            ServiceResult<T>.Ok(await action(), successMessage);
    }

    private sealed class MessageServiceStub : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) =>
            Assert.Fail($"{title}: {message}");
    }

    private sealed class DialogServiceStub : IDialogService
    {
        public bool ConfirmationResult { get; set; } = true;

        public Task ShowContentAsync(
            string title,
            object content,
            string closeButtonText = "关闭",
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> ConfirmAsync(
            string title,
            string message,
            string primaryButtonText = "确定",
            string closeButtonText = "取消",
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ConfirmationResult);

        public Task ShowErrorAsync(
            string title,
            string message,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static Task<ServiceResult<T>> NotSupported<T>() =>
        Task.FromException<ServiceResult<T>>(new NotSupportedException());
}

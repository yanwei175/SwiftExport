using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Customers.CustomerManagement.Models;
using SwiftExportClient.Features.Customers.CustomerProducts.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Customers;

[TestClass]
public sealed class CustomerProductWorkspaceTests
{
    [TestMethod]
    public async Task SelectCustomer_ShouldSearchFirstPageAndOpenEditorForPersistedCustomer()
    {
        var customerId = Guid.NewGuid();
        var customerApi = new CustomerApiStub(
        [
            new CustomerDto
            {
                Uid = customerId,
                Version = 1,
                CustomerCode = "C001",
                CustomerName = "客户一",
            },
        ]);
        var customerProductApi = new CustomerProductApiStub();
        using var workspace = CreateWorkspace(
            customerApi,
            customerProductApi,
            new DialogServiceStub());

        Assert.IsFalse(workspace.Editor.IsOpen);
        Assert.IsEmpty(customerProductApi.Requests);

        await workspace.SearchCustomersCommand.ExecuteAsync("C00");

        Assert.HasCount(1, customerApi.Requests);
        Assert.AreEqual("C00", customerApi.Requests[0].Keyword);
        Assert.AreEqual(1, customerApi.Requests[0].PageIndex);
        Assert.AreEqual(20, customerApi.Requests[0].PageSize);
        Assert.HasCount(1, workspace.CustomerCandidates);

        workspace.SelectedCustomer = workspace.CustomerCandidates[0];
        await workspace.CommitCustomerCommand.ExecuteAsync(null);

        Assert.IsTrue(workspace.Editor.IsOpen);
        Assert.AreEqual(customerId, workspace.Editor.ActiveCustomer?.Uid);
        Assert.HasCount(1, customerProductApi.Requests);
        Assert.AreEqual(customerId, customerProductApi.Requests[0].CustomerId);
        Assert.AreEqual("客户产品管理 - C001", workspace.DocumentTitle);
    }

    [TestMethod]
    public async Task SearchCustomers_WhenOlderResponseCompletesLast_ShouldKeepNewestCandidates()
    {
        var oldResponse = new TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerDto>>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var customerApi = new CustomerApiStub([], oldResponse);
        using var workspace = CreateWorkspace(
            customerApi,
            new CustomerProductApiStub(),
            new DialogServiceStub());

        var olderSearch = workspace.SearchCustomersCommand.ExecuteAsync("OLD");
        await workspace.SearchCustomersCommand.ExecuteAsync("NEW");
        oldResponse.SetResult(CustomerApiStub.Result(
            new CustomerDto
            {
                Uid = Guid.NewGuid(),
                Version = 1,
                CustomerCode = "OLD",
                CustomerName = "旧客户",
            }));
        await olderSearch;

        Assert.HasCount(1, workspace.CustomerCandidates);
        Assert.AreEqual("NEW", workspace.CustomerCandidates[0].CustomerCode);
    }

    [TestMethod]
    public async Task SwitchOrCloseWithDirtyEditor_WhenDiscardRejected_ShouldRestoreActiveCustomer()
    {
        var dialog = new DialogServiceStub { ConfirmationResult = false };
        var customerProductApi = new CustomerProductApiStub();
        using var workspace = CreateWorkspace(
            new CustomerApiStub([]),
            customerProductApi,
            dialog);
        var first = Customer(Guid.NewGuid(), "C001", "客户一");
        var second = Customer(Guid.NewGuid(), "C002", "客户二");

        workspace.SelectedCustomer = first;
        await workspace.CommitCustomerCommand.ExecuteAsync(null);
        workspace.Editor.Products.AddNewItem(first.Uid, first.CustomerCode);
        workspace.SelectedCustomer = second;

        await workspace.CommitCustomerCommand.ExecuteAsync(null);

        Assert.AreSame(first, workspace.SelectedCustomer);
        Assert.AreSame(first, workspace.Editor.ActiveCustomer);
        Assert.HasCount(1, customerProductApi.Requests);
        Assert.IsFalse(await workspace.CanCloseAsync(CancellationToken.None));
        Assert.IsTrue(workspace.HasUnsavedChanges);
    }

    private static CustomerProductWorkspaceViewModel CreateWorkspace(
        ICustomerApi customerApi,
        ICustomerProductApi customerProductApi,
        IDialogService dialogService)
    {
        var runner = new DirectApiCallRunner();
        var editor = new CustomerProductManagerViewModel(
            customerProductApi,
            null!,
            runner,
            new MessageServiceStub(),
            dialogService);
        return new CustomerProductWorkspaceViewModel(customerApi, runner, editor);
    }

    private static CustomerLineModel Customer(Guid uid, string code, string name)
    {
        var customer = new CustomerLineModel();
        customer.Load(new CustomerDto
        {
            Uid = uid,
            Version = 1,
            CustomerCode = code,
            CustomerName = name,
        });
        return customer;
    }

    private sealed class CustomerApiStub(
        IReadOnlyList<CustomerDto> customers,
        TaskCompletionSource<ServiceResult<PagedResponseBase<CustomerDto>>>? oldResponse = null)
        : ICustomerApi
    {
        public List<CustomerSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<CustomerDto>>> GetPagedAsync(CustomerSearchRequest request)
        {
            Requests.Add(request);
            if (string.Equals(request.Keyword, "OLD", StringComparison.Ordinal))
            {
                return oldResponse!.Task;
            }

            if (string.Equals(request.Keyword, "NEW", StringComparison.Ordinal))
            {
                return Task.FromResult(Result(new CustomerDto
                {
                    Uid = Guid.NewGuid(),
                    Version = 1,
                    CustomerCode = "NEW",
                    CustomerName = "新客户",
                }));
            }

            return Task.FromResult(Result(customers.ToArray()));
        }

        public static ServiceResult<PagedResponseBase<CustomerDto>> Result(params CustomerDto[] customers) =>
            ServiceResult<PagedResponseBase<CustomerDto>>.Ok(new PagedResponseBase<CustomerDto>
            {
                Items = customers.ToList(),
                TotalCount = customers.Length,
                PageIndex = 1,
                PageSize = 20,
            });

        public Task<ServiceResult<CustomerBatchSaveResponse>> BatchSaveAsync(CustomerBatchSaveRequest request) =>
            NotSupported<CustomerBatchSaveResponse>();
        public Task<ServiceResult<CustomerDto>> GetByIdAsync(Guid id) => NotSupported<CustomerDto>();
        public Task<ServiceResult<Guid>> CreateAsync(CustomerDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, CustomerDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
    }

    private sealed class CustomerProductApiStub : ICustomerProductApi
    {
        public List<CustomerProductSearchRequest> Requests { get; } = [];

        public Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest request)
        {
            Requests.Add(request);
            return Task.FromResult(ServiceResult<PagedResponseBase<CustomerProductDto>>.Ok(
                new PagedResponseBase<CustomerProductDto>
                {
                    Items = [],
                    TotalCount = 0,
                    PageIndex = request.PageIndex,
                    PageSize = request.PageSize,
                }));
        }

        public Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid id) => NotSupported<CustomerProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(CustomerProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, CustomerProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<List<CustomerDto>>> GetAllHaveProductCustomerList() =>
            NotSupported<List<CustomerDto>>();
        public Task<ServiceResult<CustomerProductDto>> GetByCustomerSku(Guid customerId, string sku) =>
            NotSupported<CustomerProductDto>();
        public Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(
            CustomerProductBatchSaveRequest request) =>
            NotSupported<CustomerProductBatchSaveResponse>();
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

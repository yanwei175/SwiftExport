using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Suppliers.SupplierManagement.Models;
using SwiftExportClient.Features.Suppliers.SupplierProducts.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.Suppliers;

[TestClass]
public sealed class SupplierProductDrawerLoadingTests
{
    [TestMethod]
    public async Task FirstOpen_ShouldLoadSelectedSupplierWithoutProductPageInitialization()
    {
        var supplierId = Guid.NewGuid();
        var api = new SupplierProductApiStub();
        var viewModel = new SupplierProductManagerViewModel(
            api, null!, new DirectApiCallRunner(), new MessageServiceStub(), new DialogServiceStub());
        var supplier = CreateSupplier(supplierId, "S001", "供应商一");

        await viewModel.ToggleCommand.ExecuteAsync(supplier);

        Assert.IsTrue(viewModel.IsOpen);
        Assert.AreSame(supplier, viewModel.ActiveSupplier);
        Assert.IsNotNull(api.LastRequest);
        Assert.AreEqual(supplierId, api.LastRequest.SupplierId);
        Assert.HasCount(1, viewModel.Products.Items);
        Assert.AreEqual("SP-1", viewModel.Products.Items[0].SupplierSku);
        Assert.AreEqual(1, viewModel.TotalCount);
    }

    [TestMethod]
    public async Task OpenDrawer_WhenSupplierChanges_ShouldReplaceResultsWithNewSupplier()
    {
        var first = CreateSupplier(Guid.NewGuid(), "S001", "供应商一");
        var second = CreateSupplier(Guid.NewGuid(), "S002", "供应商二");
        var api = new SupplierProductApiStub();
        var viewModel = new SupplierProductManagerViewModel(
            api, null!, new DirectApiCallRunner(), new MessageServiceStub(), new DialogServiceStub());

        await viewModel.ToggleCommand.ExecuteAsync(first);
        viewModel.NotifySupplierSelection(second);
        await WaitForRequestCountAsync(api, 2);

        Assert.AreSame(second, viewModel.ActiveSupplier);
        Assert.AreEqual(first.Uid, api.Requests[0].SupplierId);
        Assert.AreEqual(second.Uid, api.Requests[1].SupplierId);
        Assert.HasCount(1, viewModel.Products.Items);
        Assert.AreEqual("SP-S002", viewModel.Products.Items[0].SupplierSku);
        Assert.IsFalse(viewModel.Products.Items.Any(item => item.SupplierId == first.Uid));
    }

    [TestMethod]
    public async Task SetProductDefaultSupplier_ShouldUseProductVersionAndRefreshCurrentPage()
    {
        var supplierId = Guid.NewGuid();
        var api = new SupplierProductApiStub();
        var productApi = CreateProductApi(new ProductDto
        {
            Uid = api.ProductId,
            Version = 7,
            Sku = "P-1",
        });
        var viewModel = new SupplierProductManagerViewModel(
            api, productApi, new DirectApiCallRunner(), new MessageServiceStub(), new DialogServiceStub());

        await viewModel.ToggleCommand.ExecuteAsync(CreateSupplier(supplierId, "S001", "供应商一"));
        Assert.IsTrue(viewModel.SetProductDefaultSupplierCommand.CanExecute(null));

        await viewModel.SetProductDefaultSupplierCommand.ExecuteAsync(null);

        Assert.IsNotNull(api.LastSetDefaultCommand);
        Assert.AreEqual(api.SupplierProductId, api.LastSetDefaultCommand.SupplierProductId);
        Assert.AreEqual(api.ProductId, api.LastSetDefaultCommand.ProductId);
        Assert.AreEqual(supplierId, api.LastSetDefaultCommand.SupplierId);
        Assert.AreEqual(7, api.LastSetDefaultCommand.ExpectedProductVersion);
        Assert.AreEqual(2, api.Requests.Count, "设置成功后应刷新当前供应商产品页。");
        Assert.IsTrue(viewModel.Products.Items.Single().IsProductDefaultSupplier);
        StringAssert.Contains(viewModel.StatusText, "默认供应商已设置");
        Assert.IsFalse(viewModel.SetProductDefaultSupplierCommand.CanExecute(null));
    }

    private static SupplierLineModel CreateSupplier(Guid uid, string code, string name)
    {
        var supplier = new SupplierLineModel();
        supplier.Load(new SupplierDto
        {
            Uid = uid, Version = 1, SupplierCode = code, SupplierName = name,
            DocRelateNo = $"DOC-{code}", Currency = "CNY",
        });
        return supplier;
    }

    private static async Task WaitForRequestCountAsync(SupplierProductApiStub api, int expected)
    {
        for (var attempt = 0; attempt < 100 && api.Requests.Count < expected; attempt++) await Task.Delay(10);
        Assert.AreEqual(expected, api.Requests.Count, "切换供应商后没有发起新的供应商产品查询。");
    }

    private sealed class SupplierProductApiStub : ISupplierProductApi
    {
        public Guid ProductId { get; } = Guid.NewGuid();
        public Guid SupplierProductId { get; } = Guid.NewGuid();
        public List<SupplierProductSearchRequest> Requests { get; } = [];
        public SupplierProductSearchRequest? LastRequest => Requests.LastOrDefault();
        public SetProductDefaultSupplierCommand? LastSetDefaultCommand { get; private set; }
        private bool IsDefaultSupplier { get; set; }

        public Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync(SupplierProductSearchRequest request)
        {
            Requests.Add(request);
            var code = Requests.Count == 1 ? "S001" : "S002";
            return Task.FromResult(ServiceResult<PagedResponseBase<SupplierProductDto>>.Ok(new PagedResponseBase<SupplierProductDto>
            {
                Items =
                [
                    new SupplierProductDto
                    {
                        Uid = SupplierProductId, Version = 1, SupplierId = request.SupplierId,
                        SupplierCode = code, SupplierName = code, SkuId = ProductId,
                        ProductSku = "P-1", SupplierSku = Requests.Count == 1 ? "SP-1" : $"SP-{code}",
                        IsProductDefaultSupplier = IsDefaultSupplier,
                    },
                ],
                TotalCount = 1, PageIndex = request.PageIndex, PageSize = request.PageSize,
            }));
        }

        public Task<ServiceResult<SupplierProductDto>> GetByIdAsync(Guid id) => NotSupported<SupplierProductDto>();
        public Task<ServiceResult<Guid>> CreateAsync(SupplierProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> UpdateAsync(Guid id, SupplierProductDto request) => NotSupported<Guid>();
        public Task<ServiceResult<Guid>> DeleteAsync(Guid id) => NotSupported<Guid>();
        public Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync(SupplierProductBatchSaveRequest request) => NotSupported<SupplierProductBatchSaveResponse>();
        public Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(SetProductDefaultSupplierCommand command)
        {
            LastSetDefaultCommand = command;
            IsDefaultSupplier = true;
            return Task.FromResult(ServiceResult<SetProductDefaultSupplierResult>.Ok(new SetProductDefaultSupplierResult
            {
                ProductId = command.ProductId,
                SupplierId = command.SupplierId,
                SupplierCode = "S001",
                OldProductVersion = command.ExpectedProductVersion,
                NewProductVersion = command.ExpectedProductVersion + 1,
            }));
        }

        private static Task<ServiceResult<T>> NotSupported<T>() =>
            Task.FromException<ServiceResult<T>>(new NotSupportedException());
    }

    private static IProductApi CreateProductApi(ProductDto product)
    {
        var api = DispatchProxy.Create<IProductApi, ProductApiProxy>();
        ((ProductApiProxy)(object)api).Product = product;
        return api;
    }

    public class ProductApiProxy : DispatchProxy
    {
        public ProductDto Product { get; set; } = new();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            if (targetMethod?.Name == "GetByIdAsync")
            {
                return Task.FromResult(ServiceResult<ProductDto>.Ok(Product));
            }

            throw new NotSupportedException(targetMethod?.Name);
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

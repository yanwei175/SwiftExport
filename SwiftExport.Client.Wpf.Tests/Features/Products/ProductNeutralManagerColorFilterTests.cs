using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Responses;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.IO;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证 NeutralManager 颜色筛选快照及图片路径重建命令。</summary>
[TestClass]
public sealed class ProductNeutralManagerColorFilterTests
{
    /// <summary>临时选择不影响刷新；显式查询成功后才成为已应用筛选，失败时恢复旧选择。</summary>
    [TestMethod]
    public async Task Search_ShouldApplyColorSelectionOnlyAfterSuccess()
    {
        var api = new RecordingNeutralManagerApi();
        using var viewModel = new ProductNeutralManagerViewModel(
            api,
            new UnusedProductMasterApi(),
            new PassThroughApiCallRunner(),
            new EmptyReferenceDataCache(),
            new SilentMessageService(),
            new AcceptDialogService(),
            Options.Create(new ProductOptions()));
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();

        viewModel.ColorFilter = new ProductColorFilterSelection([first]);
        Assert.IsEmpty(api.Requests);
        await ((IAsyncRelayCommand)viewModel.SearchCommand).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { first }, api.Requests.Last().ColorIds);

        viewModel.ColorFilter = new ProductColorFilterSelection([second]);
        await ((IAsyncRelayCommand)viewModel.RefreshCommand).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { first }, api.Requests.Last().ColorIds,
            "刷新应继续使用上一次成功应用的颜色快照。");

        api.FailNext = true;
        await ((IAsyncRelayCommand)viewModel.SearchCommand).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { first }, viewModel.ColorFilter.ColorIds.ToArray(),
            "查询失败后应恢复上一次成功应用的颜色选择。");
    }

    /// <summary>单行命令应沿用主型号页规则，用配置目录和产品编码生成 JPG 图片路径。</summary>
    [TestMethod]
    public void ReBuildImagePath_ShouldUpdateSpecifiedLine()
    {
        var options = Options.Create(new ProductOptions { BaseImageFolder = @"D:\ProductImages" });
        using var viewModel = CreateViewModel(new AcceptDialogService(), new SilentMessageService(), options);
        var line = viewModel.Lines.AddNewNeutral();
        line.ProductCode = "NEUTRAL-001";

        viewModel.ReBuildImagePathCommand.Execute(line);

        Assert.AreEqual(Path.Combine(options.Value.BaseImageFolder, "NEUTRAL-001.jpg"), line.ImagePath);
    }

    /// <summary>批量命令应先确认，仅修改勾选且有产品编码的行，并报告跳过数量。</summary>
    [TestMethod]
    public async Task ReBuildCheckedImagePaths_ShouldConfirmAndOnlyUpdateEligibleCheckedLines()
    {
        var dialogs = new AcceptDialogService { ConfirmResult = false };
        var messages = new SilentMessageService();
        var options = Options.Create(new ProductOptions { BaseImageFolder = @"D:\ProductImages" });
        using var viewModel = CreateViewModel(dialogs, messages, options);
        var checkedLine = viewModel.Lines.AddNewNeutral();
        checkedLine.ProductCode = "NEUTRAL-001";
        checkedLine.ImagePath = "original.jpg";
        checkedLine.IsChecked = true;
        var blankCodeLine = viewModel.Lines.AddNewNeutral();
        blankCodeLine.ImagePath = "blank-original.jpg";
        blankCodeLine.IsChecked = true;
        var uncheckedLine = viewModel.Lines.AddNewNeutral();
        uncheckedLine.ProductCode = "NEUTRAL-003";
        uncheckedLine.ImagePath = "unchecked-original.jpg";

        await viewModel.ReBuildCheckedImagePathsCommand.ExecuteAsync(null);

        Assert.AreEqual("original.jpg", checkedLine.ImagePath, "取消确认时不得修改任何图片路径。");

        dialogs.ConfirmResult = true;
        await viewModel.ReBuildCheckedImagePathsCommand.ExecuteAsync(null);

        Assert.AreEqual(Path.Combine(options.Value.BaseImageFolder, "NEUTRAL-001.jpg"), checkedLine.ImagePath);
        Assert.AreEqual("blank-original.jpg", blankCodeLine.ImagePath);
        Assert.AreEqual("unchecked-original.jpg", uncheckedLine.ImagePath);
        Assert.AreEqual(2, dialogs.ConfirmCount);
        StringAssert.Contains(messages.LastSuccessMessage, "已重建 1 条");
        StringAssert.Contains(messages.LastSuccessMessage, "1 条因产品编码为空已跳过");
    }

    private static ProductNeutralManagerViewModel CreateViewModel(
        AcceptDialogService dialogs,
        SilentMessageService messages,
        IOptions<ProductOptions> options) => new(
        new RecordingNeutralManagerApi(),
        new UnusedProductMasterApi(),
        new PassThroughApiCallRunner(),
        new EmptyReferenceDataCache(),
        messages,
        dialogs,
        options);

    private sealed class RecordingNeutralManagerApi : IProductNeutralManagerApi
    {
        public List<ProductNeutralManagerSearchRequest> Requests { get; } = [];
        public bool FailNext { get; set; }

        public Task<ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>> GetPagedAsync(
            ProductNeutralManagerSearchRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (FailNext)
            {
                FailNext = false;
                return Task.FromResult(ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>.Fail("测试失败"));
            }

            return Task.FromResult(ServiceResult<PagedResponseBase<ProductNeutralManagerListItemDto>>.Ok(new()
            {
                PageIndex = request.PageIndex,
                PageSize = request.PageSize,
                TotalCount = 0,
                Items = [],
            }));
        }

        public Task<ServiceResult<ProductNeutralManagerBatchSaveResponse>> BatchSaveAsync(
            ProductNeutralManagerBatchSaveRequest request,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PassThroughApiCallRunner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => throw new NotSupportedException();
    }

    private sealed class UnusedProductMasterApi : IProductMasterApi
    {
        public Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class EmptyReferenceDataCache : IReferenceDataCacheService
    {
        public ReadOnlyObservableCollection<SysDicCurrencyDto> Currencies { get; } = Empty<SysDicCurrencyDto>();
        public ReadOnlyObservableCollection<ProductColorDto> ProductColors { get; } = Empty<ProductColorDto>();
        public ReadOnlyObservableCollection<CustomerDto> Customers { get; } = Empty<CustomerDto>();
        public ReadOnlyObservableCollection<SupplierDto> Suppliers { get; } = Empty<SupplierDto>();
        public ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto> WarehouseSites { get; } = Empty<SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker.WarehouseSitePickerDto>();
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategoryTree { get; } = Empty<ProductCategoryCacheNode>();
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> ProductCategories { get; } = Empty<ProductCategoryCacheNode>();
        public ReadOnlyObservableCollection<SysDictTypeDto> DictionaryTypes { get; } = Empty<SysDictTypeDto>();
        public ReadOnlyObservableCollection<SysDictDataDto> DictionaryValues { get; } = Empty<SysDictDataDto>();
        public IReadOnlyList<SysDictDataDto> GetDictionaryValues(string dictCode, bool enabledOnly = true) => [];
        public Task<CacheRefreshResult> EnsureLoadedAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshAllAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshProductColorsAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshCustomersAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshSuppliersAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> EnsureWarehouseSitesLoadedAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshWarehouseSitesAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshProductCategoriesAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshCurrenciesAsync() => throw new NotSupportedException();
        public Task<CacheRefreshResult> RefreshDictionariesAsync() => throw new NotSupportedException();
        public void Clear() { }

        private static ReadOnlyObservableCollection<T> Empty<T>() => new(new ObservableCollection<T>());
    }

    private sealed class SilentMessageService : IMessageService
    {
        public string LastSuccessMessage { get; private set; } = string.Empty;

        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) => LastSuccessMessage = message;
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }

    private sealed class AcceptDialogService : IDialogService
    {
        public bool ConfirmResult { get; set; } = true;
        public int ConfirmCount { get; private set; }

        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        {
            ConfirmCount++;
            return Task.FromResult(ConfirmResult);
        }
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}

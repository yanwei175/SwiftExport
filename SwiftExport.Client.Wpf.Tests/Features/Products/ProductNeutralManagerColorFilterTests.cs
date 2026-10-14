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
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.IO;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Mvvm;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证中性产品工具栏筛选快照、中文分类显示及图片路径重建命令。</summary>
[TestClass]
public sealed class ProductNeutralManagerColorFilterTests
{
    /// <summary>父分类包含后代且重复勾选去重；失败恢复已应用集合，清空恢复全部分类。</summary>
    [TestMethod]
    public async Task Categories_ShouldExpandDescendantsAndRestoreAfterFailure()
    {
        var rootId = Guid.NewGuid(); var childId = Guid.NewGuid(); var otherId = Guid.NewGuid();
        var cache = new ReferenceDataCacheServiceStub(productCategories:
        [
            new() { Uid = rootId, CategoryName = "ROOT", FakeName = "卫浴" },
            new() { Uid = childId, ParentId = rootId, CategoryName = "SHOWER", FakeName = "花洒" },
            new() { Uid = otherId, CategoryName = "PARTS", FakeName = "配件" }
        ]);
        var api = new RecordingNeutralManagerApi();
        using var model = new ProductNeutralManagerViewModel(api, new UnusedProductMasterApi(), new PassThroughApiCallRunner(),
            cache, new SilentMessageService(), new AcceptDialogService(), new ImageAddressResolverStub());
        var root = cache.ProductCategories.Single(item => item.Uid == rootId);
        var child = cache.ProductCategories.Single(item => item.Uid == childId);
        root.AddChild(child);
        model.FilterCategories.Add(root);
        Assert.IsEmpty(api.Requests, "临时修改集合不能提前查询。");
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        CollectionAssert.AreEquivalent(new[] { rootId, childId }, api.Requests.Single().CategoryIds);
        model.FilterCategories.Add(child);
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.HasCount(1, api.Requests, "父子重复勾选不应改变查询范围或重复请求。");

        model.FilterCategories.Clear(); model.FilterCategories.Add(cache.ProductCategories.Single(item => item.Uid == otherId));
        await model.RefreshCommand.ExecuteAsync(null);
        CollectionAssert.AreEquivalent(new[] { rootId, childId }, api.Requests.Last().CategoryIds,
            "刷新只使用上一次成功应用的分类条件。");
        api.FailNext = true;
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { root, child }, model.FilterCategories.ToArray());
        model.FilterCategories.Clear(); model.FilterCategories.Add(ProductCategoryFilterSelection.UncategorizedOption);
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.IsEmpty(api.Requests.Last().CategoryIds);
        Assert.IsTrue(api.Requests.Last().IncludeUncategorized, "单独勾选应保留仅未分类查询。");
        model.FilterCategories.Clear();
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.IsEmpty(api.Requests.Last().CategoryIds);
        Assert.IsFalse(api.Requests.Last().IncludeUncategorized);
    }

    /// <summary>取消放弃编辑时保留草稿并恢复分类和未分类选择，不发出新分页请求。</summary>
    [TestMethod]
    public async Task Categories_ShouldPreserveDraftWhenDiscardIsCancelled()
    {
        var id = Guid.NewGuid();
        var cache = new ReferenceDataCacheServiceStub(productCategories: [new() { Uid = id, FakeName = "花洒" }]);
        var api = new RecordingNeutralManagerApi();
        var dialogs = new AcceptDialogService { ConfirmResult = false };
        using var model = new ProductNeutralManagerViewModel(api, new UnusedProductMasterApi(), new PassThroughApiCallRunner(),
            cache, new SilentMessageService(), dialogs, new ImageAddressResolverStub());
        model.FilterCategories.Add(cache.ProductCategories.Single());
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        var draft = model.Lines.AddNewNeutral(); draft.ProductCode = "DRAFT";
        model.FilterCategories.Clear(); model.FilterCategories.Add(ProductCategoryFilterSelection.UncategorizedOption);
        await model.ApplySelectedCategoriesCommand.ExecuteAsync(null);
        Assert.HasCount(1, api.Requests);
        Assert.AreEqual(id, model.FilterCategories.Single().Uid);
        Assert.IsFalse(model.IncludeUncategorized);
        Assert.AreSame(draft, model.Lines.Items.Single());
        Assert.IsTrue(model.HasUnsavedChanges);
    }

    /// <summary>分页及主型号重新归属优先显示中文分类，展示回填不制造脏状态，撤销仍保持中文基线。</summary>
    [TestMethod]
    public async Task CategoryDisplay_ShouldUseChineseCacheOnLoadAndReassignment()
    {
        var id = Guid.NewGuid();
        var cache = new ReferenceDataCacheServiceStub(productCategories: [new() { Uid = id, CategoryName = "SHOWER", FakeName = "花洒" }]);
        var api = new RecordingNeutralManagerApi
        {
            Items = [new() { Uid = Guid.NewGuid(), Version = 1, CategoryId = id, CategoryName = "SHOWER",
                Data = new() { ProductMasterId = Guid.NewGuid(), ProductCode = "N-001", ColorId = Guid.NewGuid() } }]
        };
        using var model = new ProductNeutralManagerViewModel(api, new UnusedProductMasterApi(), new PassThroughApiCallRunner(),
            cache, new SilentMessageService(), new AcceptDialogService(), new ImageAddressResolverStub());
        await model.SearchCommand.ExecuteAsync(null);
        var line = model.Lines.Items.Single();
        Assert.AreEqual("花洒", line.CategoryName);
        Assert.AreEqual(LineState.Unchanged, line.State);
        model.EditProductMaster = new ProductMasterLookupItemModel(new() { Uid = Guid.NewGuid(), CategoryName = "SHOWER",
            Data = new() { MasterSku = "NEW", CategoryId = id } });
        model.CommitProductMasterCommand.Execute(line);
        Assert.AreEqual("花洒", line.CategoryName);
        Assert.AreEqual(LineState.Modified, line.State);
        model.RejectListChangesCommand.Execute(null);
        Assert.AreEqual("花洒", line.CategoryName);
        Assert.AreEqual(LineState.Unchanged, line.State);
    }

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
            new ImageAddressResolverStub());
        var first = new ProductColorDto { Uid = Guid.NewGuid(), ColorNameZh = "红色" };
        var second = new ProductColorDto { Uid = Guid.NewGuid(), ColorNameZh = "蓝色" };

        viewModel.FilterColorList.Add(first);
        Assert.IsEmpty(api.Requests);
        await ((IAsyncRelayCommand)viewModel.SearchCommand).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { first.Uid }, api.Requests.Last().ColorIds);

        viewModel.FilterColorList.Clear();
        viewModel.FilterColorList.Add(second);
        await ((IAsyncRelayCommand)viewModel.RefreshCommand).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { first.Uid }, api.Requests.Last().ColorIds,
            "刷新应继续使用上一次成功应用的颜色快照。");

        api.FailNext = true;
        await ((IAsyncRelayCommand)viewModel.SearchCommand).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { first }, viewModel.FilterColorList.ToArray(),
            "查询失败后应恢复上一次成功应用的颜色选择。");
    }

    /// <summary>单行命令使用公开基址和完整编码，保留点号并转义特殊字符。</summary>
    [TestMethod]
    [DataRow("NEUTRAL-001", "NEUTRAL-001.jpg")]
    [DataRow("ABC.001", "ABC.001.jpg")]
    [DataRow("中文 #%.001", "%E4%B8%AD%E6%96%87%20%23%25.001.jpg")]
    public void ReBuildImagePath_ShouldUpdateSpecifiedLine(string code, string escapedFileName)
    {
        var options = new ImageAddressResolverStub();
        using var viewModel = CreateViewModel(new AcceptDialogService(), new SilentMessageService(), options);
        var line = viewModel.Lines.AddNewNeutral();
        line.ProductCode = code;

        viewModel.ReBuildImagePathCommand.Execute(line);

        Assert.AreEqual(options.BaseUrl + escapedFileName, line.ImagePath);
    }

    /// <summary>本地及 UNC 重建保留完整编码和特殊字符，不做 URI 转义。</summary>
    [TestMethod]
    [DataRow(@"D:\Photos", @"D:\Photos\中文 #%.001.jpg")]
    [DataRow(@"\\server\share\图片", @"\\server\share\图片\中文 #%.001.jpg")]
    public void ReBuildImagePath_ShouldSupportLocalBase(string baseAddress, string expected)
    {
        var addresses = new ImageAddressResolverStub(baseAddress);
        using var model = CreateViewModel(new AcceptDialogService(), new SilentMessageService(), addresses);
        var line = model.Lines.AddNewNeutral();
        line.ProductCode = "中文 #%.001";
        Assert.IsTrue(model.ReBuildImagePathCommand.CanExecute(line));
        model.ReBuildImagePathCommand.Execute(line);
        Assert.AreEqual(expected, line.ImagePath);
    }

    /// <summary>缓存尚无有效基址时禁用重建，参数更新后命令直接使用新地址。</summary>
    [TestMethod]
    public void ReBuild_ShouldFollowLatestConfigurationWithoutRestart()
    {
        var addresses = new ImageAddressResolverStub(null);
        using var model = CreateViewModel(new AcceptDialogService(), new SilentMessageService(), addresses);
        var line = model.Lines.AddNewNeutral();
        line.ProductCode = "ABC.001";
        Assert.IsFalse(model.ReBuildImagePathCommand.CanExecute(line));
        addresses.BaseUrl = "https://example.test/new/";
        addresses.NotifyChanged();
        Assert.IsTrue(model.ReBuildImagePathCommand.CanExecute(line));
        model.ReBuildImagePathCommand.Execute(line);
        Assert.AreEqual("https://example.test/new/ABC.001.jpg", line.ImagePath);
        Assert.IsTrue(model.HasUnsavedChanges);
    }

    /// <summary>批量重建期间的配置通知不能让同一批行混用两个基址。</summary>
    [TestMethod]
    public async Task ReBuildBatch_ShouldFreezeBaseUriAfterConfirmation()
    {
        var addresses = new ImageAddressResolverStub();
        using var model = CreateViewModel(new AcceptDialogService(), new SilentMessageService(), addresses);
        var first = model.Lines.AddNewNeutral(); first.ProductCode = "A.001"; first.IsChecked = true;
        var second = model.Lines.AddNewNeutral(); second.ProductCode = "B.002"; second.IsChecked = true;
        first.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(first.ImagePath)) addresses.BaseUrl = "https://other.test/new/"; };
        await model.ReBuildCheckedImagePathsCommand.ExecuteAsync(null);
        Assert.AreEqual("https://example.test/files/ProductImages/A.001.jpg", first.ImagePath);
        Assert.AreEqual("https://example.test/files/ProductImages/B.002.jpg", second.ImagePath);
    }

    /// <summary>批量命令应先确认，仅修改勾选且有产品编码的行，并报告跳过数量。</summary>
    [TestMethod]
    public async Task ReBuildCheckedImagePaths_ShouldConfirmAndOnlyUpdateEligibleCheckedLines()
    {
        var dialogs = new AcceptDialogService { ConfirmResult = false };
        var messages = new SilentMessageService();
        var options = new ImageAddressResolverStub();
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

        Assert.AreEqual(options.BaseUrl + "NEUTRAL-001.jpg", checkedLine.ImagePath);
        Assert.AreEqual("blank-original.jpg", blankCodeLine.ImagePath);
        Assert.AreEqual("unchecked-original.jpg", uncheckedLine.ImagePath);
        Assert.AreEqual(2, dialogs.ConfirmCount);
        StringAssert.Contains(messages.LastSuccessMessage, "已重建 1 条");
        StringAssert.Contains(messages.LastSuccessMessage, "1 条因产品编码为空或不能作为安全文件名已跳过");
    }

    private static ProductNeutralManagerViewModel CreateViewModel(
        AcceptDialogService dialogs,
        SilentMessageService messages,
        ImageAddressResolverStub options) => new(
        new RecordingNeutralManagerApi(),
        new UnusedProductMasterApi(),
        new PassThroughApiCallRunner(),
        new EmptyReferenceDataCache(),
        messages,
        dialogs,
        options);

    /// <summary>记录页面查询并提供可控空页或分类显示数据，禁止访问真实 API。</summary>
    private sealed class RecordingNeutralManagerApi : IProductNeutralManagerApi
    {
        /// <inheritdoc />
        public Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<SwiftExport.Contracts.DTOs.Products.ProductNeutralManager.Import.ProductNeutralImportRowDto> rows,
            CancellationToken cancellationToken = default) => throw new AssertFailedException("颜色筛选不应提交导入。");
        public List<ProductNeutralManagerSearchRequest> Requests { get; } = [];
        public bool FailNext { get; set; }
        public List<ProductNeutralManagerListItemDto> Items { get; set; } = [];

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
                TotalCount = Items.Count,
                Items = Items,
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
        /// <inheritdoc />
        public ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto> ProductAttributeGroups { get; } = Empty<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto>();
        /// <inheritdoc />
        public ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeProfileGroupDto> ProductAttributeProfileRelations { get; } = Empty<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeProfileGroupDto>();
        /// <inheritdoc />
        public ProductAttributeResolution ResolveProductAttributes(Guid categoryId) => new(false, "测试未提供标签定义。", [], []);
        /// <inheritdoc />
        public bool AreProductAttributesLoaded => true;
        /// <inheritdoc />
        public event EventHandler? ProductAttributesChanged { add { } remove { } }
        /// <inheritdoc />
        public IReadOnlyList<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto> GetProductAttributeGroups(Guid categoryId) => [];
        /// <inheritdoc />
        public IReadOnlyList<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeOptionDto> GetProductAttributeOptions(Guid groupId) => [];
        /// <inheritdoc />
        public Task<CacheRefreshResult> EnsureProductAttributesLoadedAsync() => throw new NotSupportedException();
        /// <inheritdoc />
        public Task<CacheRefreshResult> RefreshProductAttributesAsync() => throw new NotSupportedException();
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

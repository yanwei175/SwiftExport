using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductNeutralManager;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Reflection;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证已有物料详情的独立选值、版本和草稿边界，不访问网络或数据库。</summary>
[TestClass]
public sealed class ProductDetailViewModelTests
{
    /// <summary>主型号引用与物料自身型号独立，编码建议和清空不联动颜色、描述与标签身份。</summary>
    [TestMethod]
    public async Task Sources_ShouldRemainIndependentAndSendNullForUneditedTags()
    {
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = Detail();
        detail.SelectedOptionIds = [definition.Options[0].Uid];
        detail.Data.ColorId = Guid.NewGuid();
        SaveProductDetailRequest? sent = null;
        using var vm = Create(detail, definition, save: request =>
        { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("保留草稿")); });
        await Open(vm, detail.Uid);
        var masterId = Guid.NewGuid(); var neutralId = Guid.NewGuid();
        vm.SelectedProductMaster = new(new() { Uid = masterId, Data = new() { MasterSku = "独立主型号", ProductAttributeProfileId = Guid.NewGuid() } });
        vm.CommitProductMasterCommand.Execute(null);
        vm.SelectedProductNeutral = new(new() { Uid = neutralId, Data = new() { ProductMasterId = Guid.NewGuid(), ProductCode = "其他主型号的中性产品" } });
        vm.CommitProductNeutralCommand.Execute(null);
        Assert.AreEqual(masterId, vm.Header.ProductMasterId);
        Assert.AreEqual("其他主型号的中性产品", vm.Header.LinkSku);
        Assert.AreEqual(detail.Data.ColorId, vm.Header.ColorId);
        Assert.IsFalse(vm.Tags.IsDirty);
        Assert.IsTrue(vm.Tags.HasSelections);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.IsNull(sent.SelectedOptionIds);
        Assert.AreEqual(masterId, sent.Data.ProductMasterId);
        Assert.AreEqual("其他主型号的中性产品", sent.Data.LinkSku);
        vm.ClearProductMasterCommand.Execute(null);
        Assert.IsNull(vm.Header.ProductMasterId);
        Assert.AreEqual("其他主型号的中性产品", vm.Header.LinkSku);
        Assert.IsTrue(vm.Tags.HasSelections);
        vm.ClearProductNeutralCommand.Execute(null);
        Assert.AreEqual(string.Empty, vm.Header.LinkSku);
        Assert.AreEqual(detail.Data.ColorId, vm.Header.ColorId);
    }

    /// <summary>失效引用只显示空编码，基本资料保存保持身份和标签，不将空显示误解释成清空引用。</summary>
    [TestMethod]
    public async Task MissingSources_ShouldPreserveIdentityAndAllowBasicSave()
    {
        var detail = Detail();
        detail.MasterSku = null; detail.Data.LinkSku = "物料自有型号";
        detail.ProductAttributeProfileId = null;
        detail.SelectedOptionIds = [Guid.NewGuid()];
        SaveProductDetailRequest? sent = null;
        using var vm = Create(detail, ProductMasterTagSelectionTests.Definition(), save: request =>
        { sent = request; return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("保留草稿")); });
        await Open(vm, detail.Uid);
        Assert.AreEqual(string.Empty, vm.Header.MasterSku);
        Assert.AreEqual("物料自有型号", vm.Header.LinkSku);
        Assert.AreEqual(detail.Data.ProductMasterId, vm.SelectedProductMaster!.Uid);
        Assert.IsNull(vm.SelectedProductNeutral);
        vm.Header.Note = "基本资料独立保存";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.AreEqual(detail.Data.ProductMasterId, sent.Data.ProductMasterId);
        Assert.AreEqual("物料自有型号", sent.Data.LinkSku);
        Assert.IsNull(sent.SelectedOptionIds);
        Assert.IsTrue(vm.Tags.HasSelections);
    }

    /// <summary>基础字段与物料自身选择通过同一请求提交，成功后才接纳版本和基线。</summary>
    [TestMethod]
    public async Task Save_ShouldSubmitFieldsAndOwnSelectionsAtomically()
    {
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = Detail();
        SaveProductDetailRequest? sent = null;
        using var vm = Create(detail, definition, save: request =>
        {
            sent = request;
            var saved = detail.Adapt<ProductDetailDto>();
            saved.Version++;
            saved.Data = request.Data;
            saved.SelectedOptionIds = request.SelectedOptionIds;
            return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Ok(new() { Detail = saved }));
        });
        await Open(vm, detail.Uid);
        vm.Header.DescriptionZh = "物料自己的描述";
        vm.Header.UnitWeight = 1.25m;
        vm.Header.UnitCbm = 0.008m;
        vm.Header.MainImage = "https://host/files/ProductImages/material.jpg";
        var group = vm.Tags.Groups.Single();
        group.SelectedItems.Add(definition.Options[0]);
        group.CommitMultipleSelectionCommand.Execute(null);
        Assert.IsTrue(vm.HasUnsavedChanges);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.AreEqual(detail.Uid, sent.ProductId);
        Assert.AreEqual(7, sent.ExpectedVersion);
        Assert.AreEqual(1.25m, sent.Data.UnitWeight);
        Assert.AreEqual(0.008m, sent.Data.UnitCBM);
        Assert.AreEqual("物料自己的描述", sent.Data.DescriptionZh);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, sent.SelectedOptionIds);
        Assert.AreEqual(8, vm.Header.Version);
        Assert.IsFalse(vm.HasUnsavedChanges);
        Assert.AreEqual(detail.ProductMasterId, vm.Source!.ProductMasterId);
        Assert.AreEqual(detail.PcsOfCarton, vm.Source.PcsOfCarton);
        Assert.AreEqual(detail.BomNo, vm.Source.BomNo);
    }

    /// <summary>定义未加载时字段保存发送 null 保持协议，失败不接纳草稿或清空已有选择。</summary>
    [TestMethod]
    public async Task UnavailableDefinitions_ShouldSendNullAndKeepFailureDraft()
    {
        var detail = Detail();
        detail.SelectedOptionIds = [Guid.NewGuid()];
        SaveProductDetailRequest? sent = null;
        using var vm = Create(detail, null, save: request =>
        {
            sent = request;
            return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("VERSION_CONFLICT", "测试冲突"));
        });
        await Open(vm, detail.Uid);
        vm.Header.DescriptionEn = "draft";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.IsNull(sent.SelectedOptionIds);
        Assert.AreEqual("draft", vm.Header.DescriptionEn);
        Assert.AreEqual(7, vm.Header.Version);
        Assert.IsTrue(vm.HasUnsavedChanges);
        Assert.IsTrue(vm.Tags.HasSelections);
        Assert.IsFalse(vm.Tags.IsDirty);
    }

    /// <summary>刷新最新来源模板只重匹配，字段、原预期版本和已确认选值均保持。</summary>
    [TestMethod]
    public async Task RefreshTags_ShouldKeepFieldsVersionAndChoicesAcrossSourceChange()
    {
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = Detail();
        detail.SelectedOptionIds = [definition.Options[0].Uid];
        var current = detail;
        var next = detail.Adapt<ProductDetailDto>();
        next.Version = 99;
        next.Data.DescriptionZh = "服务器新的描述";
        next.ProductAttributeProfileId = Guid.NewGuid();
        next.ProductAttributeProfileName = "最新来源模板";
        using var vm = Create(detail, definition, read: () => current,
            resolve: id => ProductMasterTagSelectionTests.Resolution(id == detail.ProductAttributeProfileId ? [definition] : []));
        await Open(vm, detail.Uid);
        vm.Header.DescriptionZh = "未保存资料";
        var group = vm.Tags.Groups.Single();
        group.SelectedItems.Add(definition.Options[1]);
        group.CommitMultipleSelectionCommand.Execute(null);
        current = next;
        await vm.RefreshProductAttributesCommand.ExecuteAsync(null);
        Assert.AreEqual("未保存资料", vm.Header.DescriptionZh);
        Assert.AreEqual(7, vm.Header.Version);
        Assert.AreEqual(next.ProductAttributeProfileId, vm.Source!.ProductAttributeProfileId);
        Assert.AreEqual(99, vm.Source.Version);
        CollectionAssert.AreEquivalent(definition.Options.Select(item => item.Uid).ToArray(), vm.Tags.GetSelectedOptionIds()!);
        Assert.IsTrue(vm.Tags.IsDirty);
        Assert.AreEqual(2, vm.Tags.MissingSelections.Count);
        Assert.IsNotNull(vm.Tags.ValidateSelection(next.ProductAttributeProfileId!.Value));
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(next.ProductAttributeProfileId, vm.Tags.ProfileId);
        CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, vm.Tags.GetSelectedOptionIds()!);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>定义刷新失败保持原只读来源与完整选值，不能把新来源冒充已成功解析。</summary>
    [TestMethod]
    public async Task RefreshTags_Failure_ShouldKeepSourceAndDraft()
    {
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = Detail();
        var current = detail;
        var next = detail.Adapt<ProductDetailDto>();
        next.ProductAttributeProfileId = Guid.NewGuid();
        using var vm = Create(detail, definition, read: () => current, cacheRefreshSuccess: false);
        await Open(vm, detail.Uid);
        vm.Header.Note = "备注草稿";
        current = next;
        await vm.RefreshProductAttributesCommand.ExecuteAsync(null);
        Assert.AreSame(detail, vm.Source);
        Assert.AreEqual("备注草稿", vm.Header.Note);
        Assert.AreEqual(detail.ProductAttributeProfileId, vm.Tags.ProfileId);
        Assert.AreEqual(7, vm.Header.Version);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>仅编辑标签也具备保存能力，明确清空发送空集合，拒绝快照不改版本。</summary>
    [TestMethod]
    public async Task TagsOnly_Clear_ShouldSendEmptyCollectionAndKeepVersionOnFailure()
    {
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = Detail();
        detail.SelectedOptionIds = [definition.Options[0].Uid];
        SaveProductDetailRequest? sent = null;
        using var vm = Create(detail, definition, save: request =>
        {
            sent = request;
            return Task.FromResult(ServiceResult<SaveProductDetailResponse>.Fail("测试失败"));
        });
        await Open(vm, detail.Uid);
        var group = vm.Tags.Groups.Single();
        group.SelectedItems.Clear();
        group.CommitMultipleSelectionCommand.Execute(null);
        Assert.IsFalse(vm.Header.IsDirty);
        Assert.IsTrue(vm.SaveCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.AreEqual(0, sent.SelectedOptionIds.Count);
        Assert.IsTrue(vm.Tags.IsDirty);
        Assert.AreEqual(7, vm.Header.Version);
    }

    /// <summary>只有查看权限时显示自己的标签并拒绝所有字段、Lookup和保存入口；权限恢复不会丢草稿。</summary>
    [TestMethod]
    public async Task ReadOnlyAndRevokedPermission_ShouldKeepChoicesAndBlockWrites()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsProductView, PermissionKeys.ProductsProductEdit] });
        var definition = ProductMasterTagSelectionTests.Definition();
        var detail = Detail();
        detail.SelectedOptionIds = [definition.Options[0].Uid];
        var saves = 0;
        using var vm = Create(detail, definition, user: user, save: _ =>
        { saves++; throw new AssertFailedException("无权限不能发送请求。"); });
        await Open(vm, detail.Uid);
        vm.Header.Note = "保留草稿";
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsProductView] });
        Assert.IsFalse(vm.CanEditHeader);
        Assert.IsFalse(vm.CanEditTags);
        Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        Assert.IsFalse(vm.CommitSupplierCommand.CanExecute(null));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, saves);
        Assert.AreEqual("保留草稿", vm.Header.Note);
        Assert.AreEqual("吸顶", vm.Tags.Groups.Single().SelectionSummary);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>保存期间禁止重复提交及关闭，释放或换会话后的保存响应不回写旧页面。</summary>
    [TestMethod]
    public async Task PendingSave_ShouldFreezeAndIgnoreLateSessionResponse()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = [PermissionKeys.ProductsProductView, PermissionKeys.ProductsProductEdit] });
        var detail = Detail();
        var completion = new TaskCompletionSource<ServiceResult<SaveProductDetailResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        using var vm = Create(detail, ProductMasterTagSelectionTests.Definition(), user: user, save: _ => { calls++; return completion.Task; });
        await Open(vm, detail.Uid);
        vm.Header.Note = "正在保存的草稿";
        var pending = vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);
        Assert.IsFalse(vm.CanEditHeader);
        Assert.IsFalse(await vm.CanCloseAsync(CancellationToken.None));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        user.Clear();
        var saved = detail.Adapt<ProductDetailDto>(); saved.Version = 8;
        completion.SetResult(ServiceResult<SaveProductDetailResponse>.Ok(new() { Detail = saved }));
        await pending;
        Assert.AreEqual(7, vm.Header.Version);
        Assert.AreEqual("正在保存的草稿", vm.Header.Note);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>重复激活不重新加载及覆盖草稿；释放后读响应不能建立新基线。</summary>
    [TestMethod]
    public async Task Initialize_ShouldBeIdempotentAndIgnoreDisposedRead()
    {
        var detail = Detail();
        var reads = 0;
        using var vm = Create(detail, null, read: () => { reads++; return detail; });
        await Open(vm, detail.Uid);
        vm.Header.Note = "草稿";
        await Open(vm, detail.Uid);
        Assert.AreEqual(1, reads);
        Assert.AreEqual("草稿", vm.Header.Note);
        var completion = new TaskCompletionSource<ServiceResult<ProductDetailDto>>();
        var api = Proxy<IProductApi>((method, _) => method.Name == nameof(IProductApi.GetDetailAsync) ? completion.Task : null);
        using var other = Create(detail, null, api: api);
        var loading = Open(other, detail.Uid);
        other.Dispose();
        completion.SetResult(ServiceResult<ProductDetailDto>.Ok(detail));
        await loading;
        Assert.AreEqual(Guid.Empty, other.Header.Uid);
        Assert.IsNull(other.Source);
    }

    /// <summary>列表投影不污染只读 BOM、包装、来源，详情模型空值能清除此前非空字段。</summary>
    [TestMethod]
    public void DetailMappings_ShouldPreserveReadProjectionAndClearNullableFields()
    {
        var detail = Detail();
        detail.Data.ColorId = null; detail.Data.CNYPrice = null; detail.Data.USDPrice = null;
        var projection = detail.Adapt<ProductDto>();
        Assert.AreEqual(detail.Uid, projection.Uid);
        Assert.AreEqual(detail.Version, projection.Version);
        Assert.AreEqual(detail.MasterSku, projection.MasterSku);
        Assert.AreEqual(detail.CategoryId, projection.CategoryId);
        Assert.AreEqual(detail.BomNo, projection.BomNo);
        Assert.AreEqual(detail.PcsOfCarton, projection.PcsOfCarton);
        Assert.AreEqual(Guid.Empty, projection.ColorId);
        Assert.AreEqual(0m, projection.CNYPrice);
        var line = new ProductLineModel();
        line.Load(new() { Uid = detail.Uid, Version = 6, DefaultSupplierId = Guid.NewGuid(), SupplierName = "旧供应商",
            ManufacturerId = Guid.NewGuid(), ManufacturerName = "旧制造商", DefaultWarehouseSiteId = Guid.NewGuid(), DefaultWarehouseName = "旧仓库",
            PcsOfCarton = 99m });
        line.Load(projection);
        Assert.IsNull(line.DefaultSupplierId);
        Assert.IsNull(line.ManufacturerId);
        Assert.IsNull(line.DefaultWarehouseSiteId);
        Assert.AreEqual(string.Empty, line.SupplierName);
        Assert.AreEqual(string.Empty, line.ManufacturerName);
        Assert.AreEqual(string.Empty, line.DefaultWarehouseName);
        Assert.AreEqual(detail.PcsOfCarton, line.PcsOfCarton);
        Assert.IsFalse(line.IsDirty);
        var model = new ProductEditModel();
        model.Load(detail);
        model.UnitWeight = 2m; model.DefaultSupplierId = Guid.NewGuid(); model.Finish = "旧色";
        var next = detail.Adapt<ProductDetailDto>();
        next.Data.UnitWeight = null; next.Data.DefaultSupplierId = null; next.Finish = null;
        model.Load(next);
        Assert.IsNull(model.UnitWeight);
        Assert.IsNull(model.DefaultSupplierId);
        Assert.AreEqual(string.Empty, model.Finish);
        Assert.IsFalse(model.IsDirty);
    }

    /// <summary>为纯模型与真实 STA 视图测试建立一致的窄详情依赖。</summary>
    internal static ProductDetailViewModel Create(ProductDetailDto detail, ProductAttributeGroupDto? definition,
        Func<SaveProductDetailRequest, Task<ServiceResult<SaveProductDetailResponse>>>? save = null,
        Func<ProductDetailDto>? read = null, Func<Guid, ProductAttributeResolution>? resolve = null,
        bool cacheRefreshSuccess = true, CurrentUserService? user = null, IProductApi? api = null,
        Func<IReadOnlyList<SysDictDataDto>>? units = null)
    {
        var productApi = api ?? Proxy<IProductApi>((method, args) => method.Name switch
        {
            nameof(IProductApi.GetDetailAsync) => Task.FromResult(ServiceResult<ProductDetailDto>.Ok(read?.Invoke() ?? detail)),
            nameof(IProductApi.SaveDetailAsync) => save!((SaveProductDetailRequest)args[1]!),
            nameof(IProductApi.GetDefaultWarehouseCandidatesAsync) => Task.FromResult(ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Ok(new() { Items = [], TotalCount = 0 })),
            _ => null
        });
        var cache = Proxy<IReferenceDataCacheService>((method, args) => method.Name switch
        {
            nameof(IReferenceDataCacheService.EnsureLoadedAsync) => Task.FromResult(CacheResult(true)),
            nameof(IReferenceDataCacheService.RefreshProductAttributesAsync) => Task.FromResult(CacheResult(cacheRefreshSuccess)),
            nameof(IReferenceDataCacheService.ResolveProductAttributes) => resolve?.Invoke((Guid)args[0]!)
                ?? ProductMasterTagSelectionTests.Resolution(definition is null ? [] : [definition], definition is not null),
            nameof(IReferenceDataCacheService.GetDictionaryValues) => units?.Invoke() ?? Array.Empty<SysDictDataDto>(),
            "get_ProductColors" => new ReadOnlyObservableCollection<ProductColorDto>(new ObservableCollection<ProductColorDto>()),
            _ => null
        });
        return new(productApi, null!, Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()), cache,
            Proxy<IMessageService>((_, _) => null), Proxy<IDialogService>((_, _) => Task.FromResult(true)),
            Proxy<INavigationService>((_, _) => null), user);
    }

    /// <summary>测试详情快照同时包含只读来源、包装和明确单品值。</summary>
    internal static ProductDetailDto Detail()
    {
        var masterId = Guid.NewGuid();
        return new()
        {
        Uid = Guid.NewGuid(), Version = 7, ProductMasterId = masterId, MasterSku = "MASTER",
        CategoryId = Guid.NewGuid(), CategoryName = "面盆龙头", ProductAttributeProfileId = ProductMasterTagSelectionTests.FixtureProfileId,
        ProductAttributeProfileName = "面盆龙头模板", IsActive = true, BomNo = "BOM-REFERENCE", BomVersion = 1,
        PcsOfCarton = 12m, NwOfCarton = 8m, GwOfCarton = 9m, CbmOfCarton = 0.3m,
        Data = new() { Sku = "MATERIAL", ProductMasterId = masterId, MaterialType = "成品", SupplyType = "采购", Unit = "PC", UnitWeight = 0.5m, UnitCBM = 0.01m, CNYPrice = 10m, USDPrice = 2m }
        };
    }
    /// <summary>按真实详情打开协议初始化测试文档。</summary>
    internal static Task Open(ProductDetailViewModel vm, Guid id) => vm.InitializeDocumentAsync(new ProductDocumentOpenRequest { ProductId = id }, CancellationToken.None);
    private static CacheRefreshResult CacheResult(bool success) => (CacheRefreshResult)Activator.CreateInstance(typeof(CacheRefreshResult),
        BindingFlags.Instance | BindingFlags.NonPublic, null, [success, success ? "完成" : "测试失败"], null)!;
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call; return proxy;
    }
}

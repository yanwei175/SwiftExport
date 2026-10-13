using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证管理页按需明细加载、独立保存和切换保护，不连接真实 API。</summary>
[TestClass]
public sealed class ProductMasterNeutralWorkspaceTests
{
    /// <summary>不遵守取消的旧响应也不能覆盖新选择的明细。</summary>
    [TestMethod]
    public async Task Selection_ShouldDiscardStaleResponses()
    {
        var api = new ApiStub();
        var firstResult = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>();
        var secondResult = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>();
        using var vm = CreateVm(api);
        var first = vm.Masters.Items.First();
        var second = vm.Masters.Items.Last();
        api.Load = (id, token) => id == first.Uid ? firstResult.Task : secondResult.Task;
        var firstLoad = vm.SelectMasterAsync(first);
        var firstToken = api.LastToken;
        var secondLoad = vm.SelectMasterAsync(second);
        Assert.IsTrue(firstToken.IsCancellationRequested);
        secondResult.SetResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(second.Uid, "SECOND")));
        await secondLoad;
        firstResult.SetResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(first.Uid, "OLD")));
        await firstLoad;
        Assert.AreSame(second, vm.NeutralMaster);
        Assert.AreEqual("SECOND", vm.Neutrals.Items.Single().ProductCode);
        Assert.IsTrue(vm.CanEditNeutrals);
    }

    /// <summary>取消切换保留原选择与草稿；卡片隐藏不清空草稿，确认后再换主型号。</summary>
    [TestMethod]
    public async Task Selection_ShouldProtectDirtyRowsAcrossCards()
    {
        var api = new ApiStub();
        var dialogs = new DialogStub { Result = false };
        using var vm = CreateVm(api, dialogs);
        var first = vm.Masters.Items.First();
        var second = vm.Masters.Items.Last();
        await vm.SelectMasterAsync(first);
        var edited = vm.Neutrals.Items.Single();
        edited.ProductCode = "LOCAL";
        vm.IsCardView = true;
        vm.SelectedMaster = second;
        await vm.SelectMasterAsync(second);
        Assert.AreSame(first, vm.SelectedMaster);
        Assert.AreSame(edited, vm.Neutrals.Items.Single());
        Assert.AreEqual(1, api.LoadCount);
        Assert.IsFalse(await vm.CanCloseAsync(default));
        dialogs.Result = true;
        await vm.SelectMasterAsync(second);
        Assert.AreEqual(0, vm.Neutrals.Items.Count);
        Assert.AreEqual(1, api.LoadCount, "卡片模式不预加载隐藏明细。");
        vm.IsCardView = false;
        await vm.EnsureNeutralsLoadedAsync();
        Assert.AreEqual(2, api.LoadCount);
        Assert.AreSame(second, vm.NeutralMaster);
    }

    /// <summary>明细保存只接纳下表和图片价格，上表修改及其撤销基线仍保留。</summary>
    [TestMethod]
    public async Task NeutralSave_ShouldPreserveMasterEdits()
    {
        var api = new ApiStub();
        using var vm = CreateVm(api);
        var master = vm.Masters.Items.First();
        await vm.SelectMasterAsync(master);
        master.ProductSeries = "UNSAVED";
        var line = vm.Neutrals.Items.Single();
        line.ImagePath = "new.jpg";
        line.IsMainImage = true;
        line.DefaultSalesPriceUsd = 18m;
        api.Save = (id, request) =>
        {
            Assert.AreEqual(master.Uid, id);
            Assert.AreEqual(1, request.UpdateItems.Count);
            var saved = Detail(id, request.UpdateItems[0].Data.ProductCode);
            saved.Neutrals[0].Data = request.UpdateItems[0].Data;
            saved.Neutrals[0].Version = 2;
            return Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(saved));
        };
        await vm.SaveNeutralsCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.Neutrals.HasChanges);
        Assert.AreEqual(2, vm.Neutrals.Items.Single().Version);
        Assert.AreEqual("UNSAVED", master.ProductSeries);
        Assert.AreEqual(LineState.Modified, master.State);
        Assert.AreEqual("new.jpg", master.MainImagePath);
        Assert.AreEqual(18m, master.DefaultSalesPriceUsd);
        master.RejectChanges();
        Assert.AreEqual(string.Empty, master.ProductSeries);
        Assert.AreEqual("new.jpg", master.MainImagePath);
    }

    /// <summary>保存期间禁止编辑和关闭；版本冲突后保留原行与修改。</summary>
    [TestMethod]
    public async Task FailedSave_ShouldKeepChangesAndBlockConcurrentActions()
    {
        var api = new ApiStub();
        using var vm = CreateVm(api);
        var first = vm.Masters.Items.First();
        await vm.SelectMasterAsync(first);
        var row = vm.Neutrals.Items.Single();
        row.ProductCode = "CHANGED";
        var pending = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>();
        api.Save = (_, _) => pending.Task;
        var saving = vm.SaveNeutralsCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.IsInteractionEnabled);
        Assert.IsFalse(vm.CanEditNeutrals);
        Assert.IsFalse(await vm.CanCloseAsync(default));
        await vm.SelectMasterAsync(vm.Masters.Items.Last());
        Assert.AreSame(first, vm.NeutralMaster);
        Assert.AreEqual(1, api.LoadCount);
        pending.SetResult(ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.VersionConflict, "已变化"));
        await saving;
        Assert.AreSame(row, vm.Neutrals.Items.Single());
        Assert.IsTrue(vm.Neutrals.HasChanges);
        Assert.IsTrue(vm.CanEditNeutrals);
    }

    /// <summary>父记录保存不刷新掉下表草稿；下表仍保留独立未保存状态。</summary>
    [TestMethod]
    public async Task MasterSave_ShouldNotDiscardNeutralDraft()
    {
        var api = new ApiStub();
        using var vm = CreateVm(api);
        var master = vm.Masters.Items.First();
        await vm.SelectMasterAsync(master);
        master.ProductSeries = "MASTER-EDIT";
        var row = vm.Neutrals.Items.Single();
        row.ProductCode = "NEUTRAL-EDIT";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.Masters.HasChanges);
        Assert.AreSame(row, vm.Neutrals.Items.Single());
        Assert.IsTrue(vm.Neutrals.HasChanges);
        Assert.AreEqual(0, api.PageCount);
    }

    /// <summary>新增父记录下表禁用，新增明细删除免确认并定位末行。</summary>
    [TestMethod]
    public async Task NewAndDeletedRows_ShouldRespectParentAndSelection()
    {
        var api = new ApiStub();
        var dialogs = new DialogStub();
        using var vm = CreateVm(api, dialogs);
        await vm.AddRowCommand.ExecuteAsync(null);
        Assert.IsFalse(vm.CanEditNeutrals);
        Assert.IsFalse(vm.AddNeutralCommand.CanExecute(null));
        await vm.SelectMasterAsync(vm.Masters.Items.First());
        var last = vm.Neutrals.Items.Last();
        vm.AddNeutralCommand.Execute(null);
        await vm.DeleteNeutralCommand.ExecuteAsync(null);
        Assert.AreEqual(0, dialogs.Count);
        Assert.AreSame(last, vm.SelectedNeutral);
        await vm.DeleteNeutralCommand.ExecuteAsync(null);
        Assert.AreEqual(1, dialogs.Count);
        Assert.IsNull(vm.SelectedNeutral);
        Assert.AreEqual(1, vm.Neutrals.CreateSaveRequest().DeleteItems.Count);
    }

    /// <summary>转换失败拒绝切换；查询失败不会重复请求，显式刷新可以恢复。</summary>
    [TestMethod]
    public async Task ConversionAndLoadFailure_ShouldNotReplaceContext()
    {
        var api = new ApiStub();
        using var vm = CreateVm(api);
        await vm.SelectMasterAsync(vm.Masters.Items.First());
        var original = vm.NeutralMaster;
        vm.CommitPendingEdits = () => false;
        await vm.SelectMasterAsync(vm.Masters.Items.Last());
        Assert.AreSame(original, vm.SelectedMaster);
        vm.CommitPendingEdits = null;
        api.Load = (_, _) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Fail("失败"));
        await vm.SelectMasterAsync(vm.Masters.Items.Last());
        Assert.IsFalse(vm.CanEditNeutrals);
        Assert.AreEqual(0, vm.Neutrals.Items.Count);
        await vm.EnsureNeutralsLoadedAsync();
        Assert.AreEqual(2, api.LoadCount);
        api.Load = (id, _) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(id)));
        await vm.RefreshNeutralsCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.CanEditNeutrals);
    }

    /// <summary>页面释放后迟到的读取响应不能写入集合。</summary>
    [TestMethod]
    public async Task DisposedPage_ShouldIgnoreLateResponse()
    {
        var api = new ApiStub();
        using var vm = CreateVm(api);
        var pending = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>();
        api.Load = (_, _) => pending.Task;
        var master = vm.Masters.Items.First();
        var loading = vm.SelectMasterAsync(master);
        vm.Dispose();
        pending.SetResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(master.Uid)));
        await loading;
        Assert.AreEqual(0, vm.Neutrals.Items.Count);
    }

    private static ProductMasterManagerViewModel CreateVm(ApiStub api, DialogStub? dialogs = null)
    {
        var vm = new ProductMasterManagerViewModel(api, null!, new Runner(), null!, new Messages(), dialogs ?? new DialogStub(), null!, api, new ImageAddressResolverStub(), new ProductMasterSyncDialogStub())
        { IsCardView = false };
        vm.Masters.LoadFromDtos([
            new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, Data = new ProductMasterWriteDto { MasterSku = "A" } },
            new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, Data = new ProductMasterWriteDto { MasterSku = "B" } }
        ]);
        return vm;
    }

    private static ProductMasterDetailDto Detail(Guid id, string code = "N") => new()
    {
        Uid = id, Version = 1,
        Neutrals = [new ProductNeutralDto
        {
            Uid = Guid.NewGuid(), Version = 1, ProductMasterId = id,
            Data = new ProductNeutralWriteDto { ProductCode = code, ColorId = Guid.NewGuid() }
        }]
    };

    /// <summary>可控制完成时序的接口替身，记录分页和明细调用边界。</summary>
    private sealed class ApiStub : IProductMasterApi, IProductNeutralApi
    {
        public int LoadCount { get; private set; }
        public int PageCount { get; private set; }
        public CancellationToken LastToken { get; private set; }
        public Func<Guid, CancellationToken, Task<ServiceResult<ProductMasterDetailDto>>> Load { get; set; }
            = (id, _) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(id)));
        public Func<Guid, BatchSaveRequestBase<ProductNeutralWriteDto>, Task<ServiceResult<ProductMasterDetailDto>>> Save { get; set; }
            = (_, _) => throw new AssertFailedException("未配置明细保存。");
        public Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default)
        { LoadCount++; LastToken = cancellationToken; return Load(productMasterId, cancellationToken); }
        public Task<ServiceResult<ProductMasterDetailDto>> BatchSaveAsync(Guid productMasterId, BatchSaveRequestBase<ProductNeutralWriteDto> request, CancellationToken cancellationToken = default)
            => Save(productMasterId, request);
        public Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default)
        { PageCount++; return Task.FromResult(ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new())); }
        public Task<ServiceResult<AssignProductMastersToCategoryResult>> AssignCategoryAsync(AssignProductMastersToCategoryRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("中性产品工作区不应执行主型号归类。");
        public Task<ServiceResult<ProductMasterBatchSaveResponse>> BatchSaveAsync(ProductMasterBatchSaveRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(ServiceResult<ProductMasterBatchSaveResponse>.Ok(new ProductMasterBatchSaveResponse()));
        public Task<ServiceResult<SaveProductMasterDetailResponse>> SaveDetailAsync(SaveProductMasterDetailRequest request, CancellationToken cancellationToken = default)
            => throw new AssertFailedException("管理页不应调用头部聚合保存。");
        public Task<ServiceResult<SyncProductMasterDataResponse>> SyncDescriptionsAsync(SyncProductMasterDataRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("明细测试不应下发产品资料。");
        public Task<ServiceResult<SyncProductMasterDataResponse>> SyncFeaturesAsync(SyncProductMasterDataRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("明细测试不应下发产品资料。");
        public Task<ServiceResult<SyncProductMasterDataResponse>> SyncSupplierAsync(SyncProductMasterDataRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("明细测试不应下发产品资料。");
        public Task<ServiceResult<SyncProductMasterDataResponse>> SyncWeightVolumeAsync(SyncProductMasterDataRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("明细测试不应下发产品资料。");
        public Task<ServiceResult<SyncProductMasterDataResponse>> SyncAllAsync(SyncProductMasterDataRequest request, CancellationToken cancellationToken = default) => throw new AssertFailedException("明细测试不应下发产品资料。");
    }

    /// <summary>直接执行接口委托，不引入网络或错误转换。</summary>
    private sealed class Runner : IApiCallRunner
    {
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, string failureMessage = "请求失败") => action();
        public Task<ServiceResult<T>> RunAsync<T>(Func<Task<ServiceResult<T>>> action, CancellationToken cancellationToken, string failureMessage = "请求失败") => action();
        public async Task<ServiceResult<T>> RunValueAsync<T>(Func<Task<T>> action, string successMessage = "成功", string failureMessage = "请求失败") => ServiceResult<T>.Ok(await action());
    }

    /// <summary>记录确认交互，不弹出真实窗口。</summary>
    private sealed class DialogStub : IDialogService
    {
        public bool Result { get; set; } = true;
        public int Count { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message, string primaryButtonText = "确定", string closeButtonText = "取消", CancellationToken cancellationToken = default)
        { Count++; return Task.FromResult(Result); }
        public Task ShowContentAsync(string title, object content, string closeButtonText = "关闭", CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task ShowErrorAsync(string title, string message, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    /// <summary>隔离通知 UI，测试以状态和调用结果断言。</summary>
    private sealed class Messages : IMessageService
    {
        public void ShowInfo(string title, string message) { }
        public void ShowSuccess(string title, string message) { }
        public void ShowWarning(string title, string message) { }
        public void ShowError(string title, string message) { }
    }
}

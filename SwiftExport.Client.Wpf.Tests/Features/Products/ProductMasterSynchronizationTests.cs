using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Controls;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Authentication;
using SwiftExport.Contracts.DTOs.Auth;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证两种主型号保存的同步授权、取消、互斥和失败保留，不访问网络。</summary>
[TestClass]
public sealed class ProductMasterSynchronizationTests
{
    /// <summary>五个独立命令按勾选优先、当前行回退提交同一主型号集合，并在确认后调用各自端点。</summary>
    [TestMethod]
    [DataRow("descriptions", nameof(IProductMasterApi.SyncDescriptionsAsync))]
    [DataRow("features", nameof(IProductMasterApi.SyncFeaturesAsync))]
    [DataRow("supplier", nameof(IProductMasterApi.SyncSupplierAsync))]
    [DataRow("weight-volume", nameof(IProductMasterApi.SyncWeightVolumeAsync))]
    [DataRow("all", nameof(IProductMasterApi.SyncAllAsync))]
    public async Task IndependentSync_UsesCheckedRowsAndDedicatedApi(string group, string expectedMethod)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var calls = 0;
        SyncProductMasterDataRequest? sent = null;
        string? actualMethod = null;
        var api = Proxy<IProductMasterApi>((method, args) =>
        {
            calls++;
            actualMethod = method.Name;
            sent = (SyncProductMasterDataRequest)args[0]!;
            return Task.FromResult(ServiceResult<SyncProductMasterDataResponse>.Ok(new()
            { ProductMasterCount = sent.ProductMasterIds.Count, UpdatedProductCount = 3 }));
        });
        var confirms = 0;
        var dialogs = Proxy<IDialogService>((method, _) =>
        {
            if (method.Name == nameof(IDialogService.ConfirmAsync)) { confirms++; return Task.FromResult(true); }
            return null;
        });
        using var vm = SyncManager(api, dialogs, true);
        vm.Masters.LoadFromDtos(ids.Select((id, index) => new ProductMasterListItemDto
        { Uid = id, Version = 1, Data = new() { MasterSku = $"M{index}" } }));
        vm.SelectedMaster = vm.Masters.Items[0];
        vm.Masters.Items[1].IsChecked = true;
        await SyncCommand(vm, group).ExecuteAsync(null);
        Assert.AreEqual(1, confirms);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(expectedMethod, actualMethod);
        CollectionAssert.AreEqual(new[] { ids[1] }, sent!.ProductMasterIds);
        vm.Masters.Items[1].IsChecked = false;
        await SyncCommand(vm, group).ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { ids[0] }, sent!.ProductMasterIds);
    }

    /// <summary>未保存修改、无产品编辑权限和取消确认均不能发送独立下发请求。</summary>
    [TestMethod]
    public async Task IndependentSync_BlocksDirtyUnauthorizedAndCancelled()
    {
        var calls = 0;
        var api = Proxy<IProductMasterApi>((_, _) => { calls++; throw new AssertFailedException("不应发送下发请求。"); });
        var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync)
            ? Task.FromResult(false) : null);
        using var noPermission = SyncManager(api, dialogs, false);
        noPermission.Masters.LoadFromDtos([new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "A" } }]);
        noPermission.SelectedMaster = noPermission.Masters.Items[0];
        Assert.IsFalse(noPermission.SyncAllCommand.CanExecute(null));

        using var dirty = SyncManager(api, dialogs, true);
        dirty.Masters.LoadFromDtos([new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "B" } }]);
        dirty.SelectedMaster = dirty.Masters.Items[0];
        dirty.SelectedMaster.DefaultDescriptionEn = "未保存";
        Assert.IsFalse(dirty.SyncAllCommand.CanExecute(null));
        dirty.Masters.RejectChanges();
        Assert.IsTrue(dirty.SyncAllCommand.CanExecute(null));
        await dirty.SyncAllCommand.ExecuteAsync(null);
        Assert.AreEqual(0, calls);
    }

    /// <summary>一个下发请求运行期间，其他字段组命令不能再次进入。</summary>
    [TestMethod]
    public async Task IndependentSync_BusyBlocksOtherGroups()
    {
        var completion = new TaskCompletionSource<ServiceResult<SyncProductMasterDataResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var api = Proxy<IProductMasterApi>((_, _) => { calls++; return completion.Task; });
        var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync)
            ? Task.FromResult(true) : null);
        using var vm = SyncManager(api, dialogs, true);
        vm.Masters.LoadFromDtos([new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "A" } }]);
        vm.SelectedMaster = vm.Masters.Items[0];
        var pending = vm.SyncDescriptionsCommand.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        Assert.IsFalse(vm.SyncFeaturesCommand.CanExecute(null));
        await vm.SyncFeaturesCommand.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        completion.SetResult(ServiceResult<SyncProductMasterDataResponse>.Ok(new() { ProductMasterCount = 1, UpdatedProductCount = 1 }));
        await pending;
        Assert.IsFalse(vm.IsBusy);
    }

    private static ProductMasterManagerViewModel SyncManager(IProductMasterApi api, IDialogService dialogs, bool allowed) => new(
        api, null!, Runner(), null!, Messages(), dialogs, null!, null!, new ImageAddressResolverStub(),
        new ProductMasterSyncDialogStub(), Proxy<ICurrentUserService>((method, _) =>
            method.Name == nameof(ICurrentUserService.HasPermission) ? allowed : null));

    private static IAsyncRelayCommand SyncCommand(ProductMasterManagerViewModel vm, string group) => group switch
    {
        "descriptions" => vm.SyncDescriptionsCommand,
        "features" => vm.SyncFeaturesCommand,
        "supplier" => vm.SyncSupplierCommand,
        "weight-volume" => vm.SyncWeightVolumeCommand,
        _ => vm.SyncAllCommand
    };

    /// <summary>两个入口四种选项均准确传输；API 拒绝时不接纳本地编辑。</summary>
    [TestMethod]
    [DataRow(false, false, false)] [DataRow(false, true, false)]
    [DataRow(false, false, true)] [DataRow(false, true, true)]
    [DataRow(true, false, false)] [DataRow(true, true, false)]
    [DataRow(true, false, true)] [DataRow(true, true, true)]
    public async Task Save_TransmitsSelection_AndPreservesFailedEdits(bool detail, bool descriptions, bool supplier)
    {
        var dialog = new ProductMasterSyncDialogStub { Respond = () => Task.FromResult(new ProductMasterSyncDialogResult
        { Confirmed = true, UpdateProductDescriptions = descriptions, UpdateProductDefaultSupplier = supplier }) };
        object? sent = null;
        var api = SavingApi(request => sent = request);
        if (detail)
        {
            using var vm = Detail(api, dialog);
            vm.Header.ProductSeries = "修改";
            await vm.SaveCommand.ExecuteAsync(null);
            var request = (SaveProductMasterDetailRequest)sent!;
            Assert.AreEqual(descriptions, request.UpdateProductDescriptions);
            Assert.AreEqual(supplier, request.UpdateProductDefaultSupplier);
            Assert.IsTrue(vm.HasUnsavedChanges);
        }
        else
        {
            using var vm = Manager(api, dialog);
            vm.Masters.AddNewMaster().MasterSku = "A";
            vm.Masters.AddNewMaster().MasterSku = "B";
            await vm.SaveCommand.ExecuteAsync(null);
            var request = (ProductMasterBatchSaveRequest)sent!;
            Assert.AreEqual(2, request.CreateItems.Count);
            Assert.AreEqual(descriptions, request.UpdateProductDescriptions);
            Assert.AreEqual(supplier, request.UpdateProductDefaultSupplier);
            Assert.IsTrue(vm.HasUnsavedChanges);
        }
        Assert.AreEqual(1, dialog.Calls, "每批只采集一次同步授权。");
    }

    /// <summary>弹窗等待期间阻止重复保存与关闭，取消后保留草稿且不发 API。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Cancel_DoesNotSave_AndWaitingBlocksReentry(bool detail)
    {
        var completion = new TaskCompletionSource<ProductMasterSyncDialogResult>();
        var dialog = new ProductMasterSyncDialogStub { Respond = () => completion.Task };
        var calls = 0;
        var api = SavingApi(_ => calls++);
        using var manager = Manager(api, dialog);
        using var editor = Detail(api, dialog);
        manager.Masters.AddNewMaster().MasterSku = "A";
        editor.Header.ProductSeries = "修改";
        var command = detail ? editor.SaveCommand : manager.SaveCommand;
        var pending = command.ExecuteAsync(null);
        Assert.AreEqual(1, dialog.Calls);
        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(detail ? await editor.CanCloseAsync(default) : await manager.CanCloseAsync(default));
        await command.ExecuteAsync(null);
        Assert.AreEqual(1, dialog.Calls);
        completion.SetResult(new());
        await pending;
        Assert.AreEqual(0, calls);
        Assert.IsTrue(detail ? editor.HasUnsavedChanges : manager.HasUnsavedChanges);
        Assert.IsFalse(detail ? editor.IsBusy : manager.IsBusy);
    }

    /// <summary>验证失败先阻止弹窗；纯删除直接保存且不申请物料覆盖授权。</summary>
    [TestMethod]
    public async Task ValidationAndDelete_RespectDialogBoundary()
    {
        var dialog = new ProductMasterSyncDialogStub();
        object? sent = null;
        using var vm = Manager(SavingApi(request => sent = request), dialog);
        vm.Masters.AddNewMaster();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, dialog.Calls);
        Assert.IsNull(sent);
        vm.Masters.LoadFromDtos([new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "A" } }]);
        vm.Masters.Remove(vm.Masters.Items.Single());
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(0, dialog.Calls);
        Assert.AreEqual(1, ((ProductMasterBatchSaveRequest)sent!).DeleteItems.Count);
    }

    /// <summary>仅新增 Neutral 也弹确认，取消不发送聚合保存且保留明细。</summary>
    [TestMethod]
    public async Task NeutralOnlyDetail_StillRequiresConfirmation()
    {
        var dialog = new ProductMasterSyncDialogStub { Respond = () => Task.FromResult(new ProductMasterSyncDialogResult()) };
        using var vm = Detail(SavingApi(_ => Assert.Fail("取消不应保存")), dialog);
        var row = vm.Neutrals.AddNewNeutral(vm.Header.Uid);
        row.ProductCode = "N";
        row.ColorId = Guid.NewGuid();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, dialog.Calls);
        Assert.IsFalse(vm.Header.IsDirty);
        Assert.IsTrue(vm.Neutrals.HasChanges);
    }

    /// <summary>保存确认成功即接纳服务端身份；随后的查询失败只警告，不能退回未保存状态。</summary>
    [TestMethod]
    public async Task Manager_SaveSuccessThenRefreshFailure_KeepsAcceptedBaseline()
    {
        var id = Guid.NewGuid();
        var notices = new List<string>();
        var api = Proxy<IProductMasterApi>((method, args) =>
        {
            if (method.Name == nameof(IProductMasterApi.GetPagedAsync))
                return Task.FromResult(ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Fail("查询失败"));
            var request = (ProductMasterBatchSaveRequest)args[0]!;
            return Task.FromResult(ServiceResult<ProductMasterBatchSaveResponse>.Ok(new()
            {
                TotalCount = 1,
                Items = [new SaveItemResult { ClientKey = request.CreateItems.Single().ClientKey,
                    Operation = SaveOperation.Create, Success = true, Uid = id, NewVersion = 1 }]
            }));
        });
        using var vm = new ProductMasterManagerViewModel(api, null!, Runner(), null!,
            Proxy<IMessageService>((method, _) => { notices.Add(method.Name); return null; }),
            null!, null!, null!, new ImageAddressResolverStub(), new ProductMasterSyncDialogStub());
        var row = vm.Masters.AddNewMaster();
        row.MasterSku = "A";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(id, row.Uid);
        Assert.AreEqual(1, row.Version);
        Assert.IsFalse(vm.HasUnsavedChanges);
        CollectionAssert.Contains(notices, nameof(IMessageService.ShowSuccess));
        CollectionAssert.Contains(notices, nameof(IMessageService.ShowWarning));
    }

    /// <summary>真实弹窗控件每次默认不勾选，所选值只有 Primary 结果才被接纳。</summary>
    [TestMethod]
    [DataRow(false, false)] [DataRow(true, false)] [DataRow(false, true)] [DataRow(true, true)]
    public void Dialog_DefaultsAndResult(bool descriptions, bool supplier) => StaTest.RunAsync(async () =>
    {
        var confirmed = true;
        var host = Proxy<IContentDialogService>((method, args) =>
        {
            Assert.AreEqual("ShowAsync", method.Name);
            var dialog = (ContentDialog)args[0]!;
            Assert.AreEqual("确认", dialog.PrimaryButtonText);
            Assert.AreEqual("取消", dialog.CloseButtonText);
            var checks = ((StackPanel)dialog.Content).Children.OfType<CheckBox>().ToArray();
            Assert.AreEqual(2, checks.Length);
            Assert.IsFalse(checks[0].IsChecked == true);
            Assert.IsFalse(checks[1].IsChecked == true);
            checks[0].IsChecked = descriptions;
            checks[1].IsChecked = supplier;
            return Task.FromResult(confirmed ? ContentDialogResult.Primary : ContentDialogResult.None);
        });
        var service = new ProductMasterSyncDialogService(host);
        var result = await service.ConfirmSaveAsync();
        Assert.IsTrue(result.Confirmed);
        Assert.AreEqual(descriptions, result.UpdateProductDescriptions);
        Assert.AreEqual(supplier, result.UpdateProductDefaultSupplier);
        confirmed = false;
        var cancelled = await service.ConfirmSaveAsync();
        Assert.IsFalse(cancelled.Confirmed);
        Assert.IsFalse(cancelled.UpdateProductDescriptions);
        Assert.IsFalse(cancelled.UpdateProductDefaultSupplier);
    });

    /// <summary>旧请求缺省开关为 false，新请求开关可往返序列化，不扩大通用包装。</summary>
    [TestMethod]
    public void Contracts_DefaultFalse_AndRoundTrip()
    {
        var batch = JsonConvert.DeserializeObject<ProductMasterBatchSaveRequest>("{}")!;
        var detail = JsonConvert.DeserializeObject<SaveProductMasterDetailRequest>("{}")!;
        Assert.IsFalse(batch.UpdateProductDescriptions || batch.UpdateProductDefaultSupplier);
        Assert.IsFalse(detail.UpdateProductDescriptions || detail.UpdateProductDefaultSupplier);
        batch.UpdateProductDescriptions = batch.UpdateProductDefaultSupplier = true;
        detail.UpdateProductDescriptions = detail.UpdateProductDefaultSupplier = true;
        var batchCopy = JsonConvert.DeserializeObject<ProductMasterBatchSaveRequest>(JsonConvert.SerializeObject(batch))!;
        var detailCopy = JsonConvert.DeserializeObject<SaveProductMasterDetailRequest>(JsonConvert.SerializeObject(detail))!;
        Assert.IsTrue(batchCopy.UpdateProductDescriptions && batchCopy.UpdateProductDefaultSupplier);
        Assert.IsTrue(detailCopy.UpdateProductDescriptions && detailCopy.UpdateProductDefaultSupplier);
    }

    private static ProductMasterManagerViewModel Manager(IProductMasterApi api, ProductMasterSyncDialogStub dialog) => new(
        api, null!, Runner(), null!, Messages(), null!, null!, null!, new ImageAddressResolverStub(), dialog);

    private static ProductMasterDetailViewModel Detail(IProductMasterApi api, ProductMasterSyncDialogStub dialog)
    {
        var vm = new ProductMasterDetailViewModel(api, null!, Runner(),
            Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
            {
                "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>()),
                "get_ProductAttributeGroups" => new ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto>(new ObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto>()),
                "get_AreProductAttributesLoaded" => false,
                nameof(IReferenceDataCacheService.ResolveProductAttributes) => new ProductAttributeResolution(true, string.Empty, [], []),
                _ => null
            }),
            Messages(), null!, null!, dialog);
        vm.Header.Load(new ProductMasterDetailDto { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "A" } });
        return vm;
    }

    private static IProductMasterApi SavingApi(Action<object> capture) => Proxy<IProductMasterApi>((method, args) =>
    {
        capture(args[0]!);
        return method.Name == nameof(IProductMasterApi.SaveDetailAsync)
            ? (object)Task.FromResult(ServiceResult<SaveProductMasterDetailResponse>.Fail("VersionConflict", "测试冲突"))
            : Task.FromResult(ServiceResult<ProductMasterBatchSaveResponse>.Fail("VersionConflict", "测试冲突"));
    });

    private static IApiCallRunner Runner() => Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
    private static IMessageService Messages() => Proxy<IMessageService>((_, _) => null);

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }
}

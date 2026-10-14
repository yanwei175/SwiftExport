using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主型号列表和详情直接保存，不弹出物料覆盖确认，失败保留本页草稿。</summary>
[TestClass]
public sealed class ProductMasterSynchronizationTests
{
    /// <summary>两种保存入口直接提交本页变更，不申请物料覆盖授权，失败仍保留全部编辑。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Save_DirectlySubmitsAndPreservesFailedEdits(bool detail)
    {
        object? sent = null;
        var calls = 0;
        var api = SavingApi(request => { calls++; sent = request; });
        var dialogs = RejectDialogs();
        if (detail)
        {
            using var vm = Detail(api, dialogs);
            var seriesId = Guid.NewGuid();
            vm.Header.ProductSeriesId = seriesId;
            vm.Header.DefaultDescriptionZh = "主型号自己的描述";
            await vm.SaveCommand.ExecuteAsync(null);
            var request = (SaveProductMasterDetailRequest)sent!;
            Assert.AreEqual(vm.Header.Uid, request.ProductMasterId);
            Assert.AreEqual(seriesId, request.Master.ProductSeriesId);
            Assert.AreEqual("主型号自己的描述", request.Master.DefaultDescriptionZh);
            Assert.IsTrue(vm.HasUnsavedChanges);
        }
        else
        {
            using var vm = Manager(api, dialogs);
            vm.Masters.AddNewMaster().MasterSku = "A";
            vm.Masters.AddNewMaster().MasterSku = "B";
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.AreEqual(2, ((ProductMasterBatchSaveRequest)sent!).CreateItems.Count);
            Assert.IsTrue(vm.HasUnsavedChanges);
        }
        Assert.AreEqual(1, calls);
    }

    /// <summary>请求运行期间阻止重复保存和关闭，失败结束后恢复入口且保留原草稿。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Save_RequestWaitBlocksReentryAndKeepsFailedDraft(bool detail)
    {
        var completion = new TaskCompletionSource<bool>();
        var calls = 0;
        async Task<ServiceResult<SaveProductMasterDetailResponse>> CompleteDetailAsync()
        {
            await completion.Task;
            return ServiceResult<SaveProductMasterDetailResponse>.Fail("VersionConflict", "测试冲突");
        }
        async Task<ServiceResult<ProductMasterBatchSaveResponse>> CompleteBatchAsync()
        {
            await completion.Task;
            return ServiceResult<ProductMasterBatchSaveResponse>.Fail("VersionConflict", "测试冲突");
        }
        var api = Proxy<IProductMasterApi>((method, _) =>
        {
            calls++;
            return method.Name == nameof(IProductMasterApi.SaveDetailAsync)
                ? (object)CompleteDetailAsync() : CompleteBatchAsync();
        });
        using var manager = Manager(api, RejectDialogs());
        using var editor = Detail(api, RejectDialogs());
        manager.Masters.AddNewMaster().MasterSku = "A";
        editor.Header.ProductSeriesId = Guid.NewGuid();
        var command = detail ? editor.SaveCommand : manager.SaveCommand;
        var pending = command.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        Assert.IsFalse(command.CanExecute(null));
        Assert.IsFalse(detail ? await editor.CanCloseAsync(default) : await manager.CanCloseAsync(default));
        await command.ExecuteAsync(null);
        Assert.AreEqual(1, calls);
        completion.SetResult(true);
        await pending;
        Assert.IsTrue(detail ? editor.HasUnsavedChanges : manager.HasUnsavedChanges);
        Assert.IsFalse(detail ? editor.IsBusy : manager.IsBusy);
    }

    /// <summary>无效主型号阻断请求，纯删除不需要任何额外同步确认。</summary>
    [TestMethod]
    public async Task ValidationAndDelete_SaveWithoutAdditionalConfirmation()
    {
        object? sent = null;
        using var vm = Manager(SavingApi(request => sent = request), RejectDialogs());
        vm.Masters.AddNewMaster();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNull(sent);
        vm.Masters.LoadFromDtos([new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "A" } }]);
        vm.Masters.Remove(vm.Masters.Items.Single());
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, ((ProductMasterBatchSaveRequest)sent!).DeleteItems.Count);
    }

    /// <summary>只新增中性产品也直接发送聚合保存，失败保留明细且不修改头部基线。</summary>
    [TestMethod]
    public async Task NeutralOnlyDetail_DirectlySavesAndPreservesFailedDraft()
    {
        SaveProductMasterDetailRequest? sent = null;
        using var vm = Detail(SavingApi(request => sent = (SaveProductMasterDetailRequest)request), RejectDialogs());
        var row = vm.Neutrals.AddNewNeutral(vm.Header.Uid);
        row.ProductCode = "N";
        row.ColorId = Guid.NewGuid();
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.IsNotNull(sent);
        Assert.AreEqual(1, sent.CreateNeutrals.Count);
        Assert.IsFalse(vm.Header.IsDirty);
        Assert.IsTrue(vm.Neutrals.HasChanges);
    }

    /// <summary>保存成功即接纳服务端身份，随后刷新失败只提示，不能退回未保存状态。</summary>
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
        using var vm = new ProductMasterManagerViewModel(api, Runner(), null!,
            Proxy<IMessageService>((method, _) => { notices.Add(method.Name); return null; }),
            RejectDialogs(), null!, null!, new ImageAddressResolverStub());
        var row = vm.Masters.AddNewMaster();
        row.MasterSku = "A";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(id, row.Uid);
        Assert.AreEqual(1, row.Version);
        Assert.IsFalse(vm.HasUnsavedChanges);
        CollectionAssert.Contains(notices, nameof(IMessageService.ShowSuccess));
        CollectionAssert.Contains(notices, nameof(IMessageService.ShowWarning));
    }

    /// <summary>保存契约不再序列化物料覆盖开关，防止客户端继续携带已退休的被动下发行为。</summary>
    [TestMethod]
    public void SaveContracts_ShouldExcludeProductOverwriteOptions()
    {
        foreach (var request in new object[] { new ProductMasterBatchSaveRequest(), new SaveProductMasterDetailRequest() })
        {
            var json = JObject.Parse(JsonConvert.SerializeObject(request));
            Assert.IsNull(json["UpdateProductDescriptions"]);
            Assert.IsNull(json["UpdateProductDefaultSupplier"]);
        }
    }

    /// <summary>列表保存夹具的对话服务仅负责检测不应发生的附加确认。</summary>
    private static ProductMasterManagerViewModel Manager(IProductMasterApi api, IDialogService dialogs) => new(
        api, Runner(), null!, Messages(), dialogs, null!, null!, new ImageAddressResolverStub());

    /// <summary>详情夹具加载已保存头部，不触发导航或网络查询。</summary>
    private static ProductMasterDetailViewModel Detail(IProductMasterApi api, IDialogService dialogs)
    {
        var vm = new ProductMasterDetailViewModel(api, Runner(), new ReferenceDataCacheServiceStub(),
            Messages(), dialogs, null!);
        vm.Header.Load(new ProductMasterDetailDto { Uid = Guid.NewGuid(), Version = 1, Master = new() { MasterSku = "A" } });
        return vm;
    }

    /// <summary>返回确定失败的保存回执，验证本页草稿和版本不能被提前接纳。</summary>
    private static IProductMasterApi SavingApi(Action<object> capture) => Proxy<IProductMasterApi>((method, args) =>
    {
        capture(args[0]!);
        return method.Name == nameof(IProductMasterApi.SaveDetailAsync)
            ? (object)Task.FromResult(ServiceResult<SaveProductMasterDetailResponse>.Fail("VersionConflict", "测试冲突"))
            : Task.FromResult(ServiceResult<ProductMasterBatchSaveResponse>.Fail("VersionConflict", "测试冲突"));
    });

    private static IDialogService RejectDialogs() => Proxy<IDialogService>((_, _) =>
        throw new AssertFailedException("主型号保存不应弹出物料覆盖确认。"));
    private static IApiCallRunner Runner() => Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
    private static IMessageService Messages() => Proxy<IMessageService>((_, _) => null);

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }
}

using System.Reflection;
using CommunityToolkit.Mvvm.Messaging;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证管理页只读摘要、沿用详情入口的草稿保护，以及详情保存后的定向刷新。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterManagerTagTests
{
    /// <summary>摘要独立于系列名，不进入主型号写入或行脏状态。</summary>
    [TestMethod]
    public void Summary_IsReadOnlyProjectionAndDoesNotChangeSeries()
    {
        var row = new ProductMasterLineModel();
        row.Load(new() { Uid = Guid.NewGuid(), Version = 4, TagSummary = "尺寸：8寸", Data = new() { MasterSku = "A", ProductSeries = "原系列" } });
        Assert.AreEqual("尺寸：8寸", row.TagSummary); Assert.IsFalse(row.IsDirty);
        row.TagSummary = "尺寸：10寸";
        Assert.IsFalse(row.IsDirty); Assert.AreEqual("原系列", row.ToWriteDto().ProductSeries);
        Assert.AreEqual("尺寸：10寸", row.ToDto().TagSummary);
    }

    /// <summary>取消保留当前行，放弃只撤销目标行；其他行草稿不会被打开详情覆盖。</summary>
    [TestMethod]
    [DataRow(DialogResult.Close)]
    [DataRow(DialogResult.Secondary)]
    public async Task OpenDetail_HandlesOnlyTargetDraft(DialogResult choice)
    {
        using var fixture = new Fixture { Choice = choice };
        var target = fixture.ViewModel.Masters.Items[0]; var other = fixture.ViewModel.Masters.Items[1];
        target.DefaultDescriptionZh = "当前草稿"; other.DefaultDescriptionZh = "其他草稿";
        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(target);
        Assert.AreEqual(choice == DialogResult.Close ? "当前草稿" : "原描述", target.DefaultDescriptionZh);
        Assert.AreEqual("其他草稿", other.DefaultDescriptionZh); Assert.IsTrue(other.IsDirty);
        Assert.AreEqual(choice == DialogResult.Close ? 0 : 1, fixture.Opened.Count);
        if (fixture.Opened.Count > 0) Assert.AreEqual(target.Uid, ((ProductMasterDocumentOpenRequest)fixture.Opened.Single().Parameter!).ProductMasterId);
        Assert.AreEqual(0, fixture.SaveCount);
    }

    /// <summary>保存成功才进入详情，保存失败维持草稿及原页面。</summary>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task OpenDetail_SaveMustSucceedBeforeNavigation(bool succeeds)
    {
        using var fixture = new Fixture { Choice = DialogResult.Primary, SaveSucceeds = succeeds };
        var target = fixture.ViewModel.Masters.Items[0]; target.DefaultDescriptionZh = "待保存";
        await fixture.ViewModel.OpenDetailCommand.ExecuteAsync(target);
        Assert.AreEqual(1, fixture.SaveCount);
        Assert.AreEqual(succeeds ? 1 : 0, fixture.Opened.Count);
        Assert.AreEqual(!succeeds, fixture.ViewModel.Masters.HasChanges);
    }

    /// <summary>详情保存只重读目标投影及版本，不重新加载整张编辑集合。</summary>
    [TestMethod]
    public void SavedDetail_UpdatesTargetAndPreservesOtherDraft()
    {
        using var fixture = new Fixture(); var target = fixture.ViewModel.Masters.Items[0]; var other = fixture.ViewModel.Masters.Items[1];
        other.DefaultDescriptionZh = "其他草稿";
        fixture.DetailResponse = (_, _) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(target.Uid, 8)));
        WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(target.Uid));
        Assert.AreEqual(8, target.Version); Assert.AreEqual("安装方式：吸顶", target.TagSummary); Assert.IsFalse(target.IsDirty);
        Assert.AreSame(other, fixture.ViewModel.Masters.Items[1]); Assert.AreEqual("其他草稿", other.DefaultDescriptionZh); Assert.IsTrue(other.IsDirty);
        Assert.AreEqual(0, fixture.PageReads);
    }

    /// <summary>目标行的新草稿不被重读覆盖，也不推进其旧基线版本。</summary>
    [TestMethod]
    public async Task SavedDetail_ChangesDuringReadArePreserved()
    {
        using var fixture = new Fixture(); var target = fixture.ViewModel.Masters.Items[0];
        var pending = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailResponse = (_, _) => pending.Task;
        WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(target.Uid));
        target.DefaultDescriptionZh = "查询期间的草稿";
        pending.SetResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(target.Uid, 8)));
        await WaitForAsync(() => fixture.ViewModel.ResultSummary?.Contains("草稿") == true);
        Assert.AreEqual(1, target.Version); Assert.AreEqual("查询期间的草稿", target.DefaultDescriptionZh); Assert.IsTrue(target.IsDirty);
    }

    /// <summary>同型号旧查询和关闭后查询均不接纳，已释放页签也不会继续订阅保存通知。</summary>
    [TestMethod]
    public async Task SavedDetail_LatestRequestAndDisposalProtectProjection()
    {
        using var fixture = new Fixture(); var target = fixture.ViewModel.Masters.Items[0];
        var old = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailResponse = (_, _) => old.Task;
        WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(target.Uid));
        fixture.DetailResponse = (_, _) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(target.Uid, 9)));
        WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(target.Uid));
        Assert.AreEqual(9, target.Version);
        old.SetResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(target.Uid, 8)));
        await Task.Yield(); Assert.AreEqual(9, target.Version);
        var closing = new TaskCompletionSource<ServiceResult<ProductMasterDetailDto>>(TaskCreationOptions.RunContinuationsAsynchronously);
        fixture.DetailResponse = (_, _) => closing.Task;
        WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(target.Uid));
        fixture.ViewModel.Dispose(); closing.SetResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(target.Uid, 10)));
        await Task.Yield(); Assert.AreEqual(9, target.Version);
    }

    /// <summary>视图提交发生异常时通知入口观察失败，保留列表基线并提示手动刷新。</summary>
    [TestMethod]
    public void SavedDetail_ObservesNativeCommitFailure()
    {
        using var fixture = new Fixture();
        var target = fixture.ViewModel.Masters.Items[0];
        fixture.ViewModel.CommitPendingEdits = () => throw new InvalidOperationException("测试编辑提交失败");
        WeakReferenceMessenger.Default.Send(new ProductMasterDetailSavedMessage(target.Uid));
        Assert.IsTrue(fixture.ViewModel.ResultSummary.Contains("刷新未完成"));
        Assert.AreEqual(1, target.Version);
        Assert.AreEqual("原描述", target.DefaultDescriptionZh);
        Assert.AreEqual(0, fixture.PageReads);
    }

    /// <summary>查询开始和结束时主动通知已有详情入口，无需重新选行才恢复按钮。</summary>
    [TestMethod]
    public void OpenDetail_IsNotifiedWhenQueryStateChanges()
    {
        using var fixture = new Fixture();
        var command = fixture.ViewModel.OpenDetailCommand;
        var states = new List<bool>();
        command.CanExecuteChanged += (_, _) => states.Add(command.CanExecute(null));
        fixture.ViewModel.IsQuerying = true;
        fixture.ViewModel.IsQuerying = false;
        CollectionAssert.AreEqual(new[] { false, true }, states.ToArray());
    }

    /// <summary>提供服务端标签展示数据，避免测试复制生产摘要算法。</summary>
    private static ProductMasterDetailDto Detail(Guid id, int version) => new()
    {
        Uid = id, Version = version, Master = new() { MasterSku = "A", DefaultDescriptionZh = "服务端新描述", ProductSeries = "旧系列" },
        AttributeSelections = [new ProductMasterAttributeSelectionDto { ProductMasterId = id, OptionId = Guid.NewGuid(), AttributeGroupId = Guid.NewGuid(), GroupName = "安装方式", OptionName = "吸顶", IsAvailable = true }]
    };

    /// <summary>等待可观察状态，不使用私有字段或固定延时冒充查询完成。</summary>
    private static async Task WaitForAsync(Func<bool> completed)
    {
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (!completed() && DateTime.UtcNow < deadline) await Task.Delay(10);
        Assert.IsTrue(completed(), "异步投影未按预期完成。");
    }

    /// <summary>只模拟本场景的 API、弹窗和导航；实际页面命令负责状态变化。</summary>
    private sealed class Fixture : IDisposable
    {
        internal DialogResult Choice = DialogResult.Close;
        internal bool SaveSucceeds;
        internal int SaveCount, PageReads;
        internal readonly List<(string Key, object? Parameter)> Opened = [];
        internal readonly List<ProductMasterListItemDto> Server = [new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "A", DefaultDescriptionZh = "原描述" } },
            new() { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "B", DefaultDescriptionZh = "原描述" } }];
        internal Func<Guid, CancellationToken, Task<ServiceResult<ProductMasterDetailDto>>> DetailResponse = (id, _) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(Detail(id, 1)));
        internal readonly ProductMasterManagerViewModel ViewModel;
        internal Fixture()
        {
            var api = Proxy<IProductMasterApi>((method, args) =>
            {
                if (method.Name == nameof(IProductMasterApi.GetDetailAsync)) return DetailResponse((Guid)args[0]!, (CancellationToken)args[1]!);
                if (method.Name == nameof(IProductMasterApi.GetPagedAsync))
                {
                    PageReads++;
                    return Task.FromResult(ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(new() { Items = Server.Adapt<List<ProductMasterListItemDto>>(), PageIndex = 1, PageSize = 30, TotalCount = Server.Count }));
                }
                Assert.AreEqual(nameof(IProductMasterApi.BatchSaveAsync), method.Name); SaveCount++;
                if (!SaveSucceeds) return Task.FromResult(ServiceResult<ProductMasterBatchSaveResponse>.Fail("测试保存失败"));
                var request = (ProductMasterBatchSaveRequest)args[0]!; var response = new ProductMasterBatchSaveResponse();
                foreach (var item in request.UpdateItems)
                {
                    var saved = Server.Single(row => row.Uid == item.Uid); saved.Data = item.Data.Adapt<ProductMasterWriteDto>(); saved.Version++;
                    response.Items.Add(new SaveItemResult { Uid = item.Uid, ClientKey = item.ClientKey, NewVersion = saved.Version, Success = true, Operation = SaveOperation.Update });
                }
                return Task.FromResult(ServiceResult<ProductMasterBatchSaveResponse>.Ok(response));
            });
            var runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
            var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ShowAsync) ? Task.FromResult(Choice) : Task.FromResult(true));
            var navigation = Proxy<INavigationService>((method, args) =>
            {
                Assert.AreEqual(nameof(INavigationService.OpenDocumentAsync), method.Name);
                Opened.Add(((string)args[1]!, args[2])); return Task.CompletedTask;
            });
            ViewModel = new(api, null!, runner, new ReferenceDataCacheServiceStub(), Proxy<IMessageService>((_, _) => null), dialogs,
                navigation, null!, new ImageAddressResolverStub(), new ProductMasterSyncDialogStub());
            ViewModel.Masters.LoadFromDtos(Server.Adapt<List<ProductMasterListItemDto>>());
            ViewModel.SelectedMaster = ViewModel.Masters.Items[0];
        }
        /// <summary>解除实际页面的消息订阅，防止测试之间互相影响。</summary>
        public void Dispose() => ViewModel.Dispose();
    }

    /// <summary>复用既有 DispatchProxy 测试适配器，不创建另一套 API 测试框架。</summary>
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }
}

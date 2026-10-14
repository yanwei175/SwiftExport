using System.Collections.ObjectModel;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Models;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主型号详情与管理列表中的系列身份选择、失效保护及字典名称刷新。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterSeriesEditingTests
{
    /// <summary>系列管理导航只打开指定类型，不保存主型号或替换任何字段草稿。</summary>
    [TestMethod]
    public async Task ManageSeries_ShouldOpenTypedDocumentAndKeepMasterDraft()
    {
        using var fixture = new Fixture();
        fixture.Detail.Header.DefaultDescriptionZh = "保留详情草稿";
        await fixture.Detail.ManageSeriesCommand.ExecuteAsync(null);
        Assert.AreEqual(DictionaryDataManagerViewModel.RouteKey, fixture.LastRoute);
        Assert.AreEqual(DictCodes.ProductSeries, fixture.LastRequest!.DictCode);
        Assert.AreEqual(fixture.LastRequest.DocumentKey, fixture.LastDocumentKey);
        Assert.AreEqual(fixture.Original.Uid, fixture.Detail.Header.ProductSeriesId);
        Assert.AreEqual("保留详情草稿", fixture.Detail.Header.DefaultDescriptionZh);
        Assert.IsTrue(fixture.Detail.HasUnsavedChanges);
        Assert.AreEqual(0, fixture.SaveCalls);
    }

    /// <summary>系列管理遵循字典查看权限，权限收回和标签弹出层打开时拒绝程序性导航。</summary>
    [TestMethod]
    public async Task ManageSeries_ShouldRespectDictionaryPermissionAndOpenPopup()
    {
        var user = new CurrentUserService();
        user.SetCurrentUser(new() { Permissions = ["Products.Master.Edit", "System.Dictionary.View"] });
        using var fixture = new Fixture(user);
        Assert.IsTrue(fixture.Detail.ManageSeriesCommand.CanExecute(null));
        fixture.Detail.Tags.BeginPopupEdit();
        await fixture.Detail.ManageSeriesCommand.ExecuteAsync(null);
        Assert.IsNull(fixture.LastRoute);
        fixture.Detail.Tags.EndPopupEdit();
        user.SetCurrentUser(new() { Permissions = ["Products.Master.Edit"] });
        Assert.IsFalse(fixture.Detail.ManageSeriesCommand.CanExecute(null));
        await fixture.Detail.ManageSeriesCommand.ExecuteAsync(null);
        Assert.IsNull(fixture.LastRoute);
        user.SetCurrentUser(new() { Permissions = ["Products.Master.Edit", "System.Dictionary.View"] });
        await fixture.Detail.ManageSeriesCommand.ExecuteAsync(null);
        Assert.AreEqual(DictCodes.ProductSeries, fixture.LastRequest!.DictCode);
    }

    /// <summary>真实详情图标按钮具有可访问名称，实际点击触发公共字典页导航并保留表单。</summary>
    [TestMethod]
    public void ManageSeriesButton_ShouldBindAndOpenFromDetail() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture();
        fixture.Detail.Header.DefaultDescriptionZh = "未保存表单";
        var view = new ProductMasterDetailView { DataContext = fixture.Detail };
        var window = new Window { Content = view, Width = 1400, Height = 900, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var button = (Button)view.FindName("管理系列");
            Assert.AreSame(fixture.Detail.ManageSeriesCommand, button.Command);
            Assert.IsTrue(button.IsEnabled);
            Assert.AreEqual("管理产品系列", AutomationProperties.GetName(button));
            ((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)!).Invoke();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.AreEqual(DictCodes.ProductSeries, fixture.LastRequest!.DictCode);
            Assert.AreEqual("未保存表单", fixture.Detail.Header.DefaultDescriptionZh);
            Assert.IsTrue(fixture.Detail.HasUnsavedChanges);
            Assert.AreEqual(0, fixture.SaveCalls);
        }
        finally { window.Close(); }
    });

    /// <summary>候选暂选不修改业务身份，确认和清空才写入，撤销恢复原候选并保留其他字段草稿语义。</summary>
    [TestMethod]
    public void Detail_ShouldCommitClearAndRestoreSeriesByIdentity()
    {
        using var fixture = new Fixture();
        var vm = fixture.Detail;
        vm.Header.DefaultDescriptionZh = "其他草稿";
        vm.SelectedSeries = fixture.Next;
        Assert.AreEqual(fixture.Original.Uid, vm.Header.ProductSeriesId);
        vm.CommitSeriesCommand.Execute(null);
        Assert.AreEqual(fixture.Next.Uid, vm.Header.ToWriteDto().ProductSeriesId);
        Assert.AreEqual(fixture.Next.DataNameZh, vm.Header.ProductSeriesName);
        vm.ClearSeriesCommand.Execute(null);
        Assert.IsNull(vm.Header.ToWriteDto().ProductSeriesId);
        Assert.IsNull(vm.SelectedSeries);
        Assert.AreEqual(string.Empty, vm.Header.ProductSeriesName);
        Assert.AreEqual("其他草稿", vm.Header.DefaultDescriptionZh);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreSame(fixture.Original, vm.SelectedSeries);
        Assert.AreEqual(fixture.Original.Uid, vm.Header.ProductSeriesId);
        Assert.IsFalse(vm.HasUnsavedChanges);
        vm.SelectedSeries = new SysDictDataDto { Uid = Guid.NewGuid(), DataNameZh = "非候选" };
        vm.CommitSeriesCommand.Execute(null);
        Assert.AreEqual(fixture.Original.Uid, vm.Header.ProductSeriesId);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>服务端拒绝不会丢弃系列选择及原版本，名称刷新按 Uid 恢复候选，不污染本地草稿。</summary>
    [TestMethod]
    public async Task Detail_SaveFailureAndDictionaryRename_ShouldKeepDraftAndVersion()
    {
        using var fixture = new Fixture();
        var vm = fixture.Detail;
        vm.SelectedSeries = fixture.Next;
        vm.CommitSeriesCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, fixture.SaveCalls, "必须实际经过专用保存 API 的失败回执。");
        Assert.AreEqual(fixture.Next.Uid, vm.Header.ProductSeriesId);
        Assert.AreSame(fixture.Next, vm.SelectedSeries);
        Assert.AreEqual(1, vm.Header.Version);
        Assert.IsTrue(vm.HasUnsavedChanges);
        var replacement = new SysDictDataDto { Uid = fixture.Next.Uid, DictCode = DictCodes.ProductSeries,
            DataNameZh = "新系列名称", DataNameEn = "NEXT", IsEnabled = true };
        fixture.Series = [fixture.Original, replacement];
        fixture.PublishDictionaryChange();
        Assert.AreSame(replacement, vm.SelectedSeries);
        Assert.AreEqual("新系列名称", vm.Header.ProductSeriesName);
        Assert.IsTrue(vm.HasUnsavedChanges);
        vm.RejectDetailChangesCommand.Execute(null);
        fixture.Series = [new SysDictDataDto { Uid = fixture.Original.Uid, DictCode = DictCodes.ProductSeries,
            DataNameZh = "原系列更名", DataNameEn = "FIRST", IsEnabled = true }, replacement];
        fixture.PublishDictionaryChange();
        Assert.AreEqual("原系列更名", vm.Header.ProductSeriesName);
        Assert.IsFalse(vm.HasUnsavedChanges, "仅字典显示名称更新不得产生业务修改。");
    }

    /// <summary>缓存中的旧系列缺失时保留身份；列表仅提交目标行的有效候选，其他行草稿不会被接纳。</summary>
    [TestMethod]
    public void ManagerAndMissingCandidate_ShouldKeepIdentityAndOtherRowsDraft()
    {
        using var fixture = new Fixture();
        fixture.Series = [fixture.Next];
        fixture.PublishDictionaryChange();
        Assert.AreEqual(fixture.Original.Uid, fixture.Detail.Header.ProductSeriesId);
        Assert.AreEqual(fixture.Original.Uid, fixture.Detail.SelectedSeries!.Uid);
        Assert.IsFalse(fixture.Detail.SelectedSeries.IsEnabled);
        Assert.IsFalse(fixture.Detail.HasUnsavedChanges);
        using var manager = new ProductMasterManagerViewModel(null!, null!, fixture.Runner, fixture.Cache,
            fixture.Messages, null!, null!, null!, new ImageAddressResolverStub(), new ProductMasterSyncDialogStub());
        manager.Masters.LoadFromDtos([
            new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "A" } },
            new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, Data = new() { MasterSku = "B" } }]);
        var target = manager.Masters.Items[0];
        var other = manager.Masters.Items[1];
        other.DefaultDescriptionZh = "其他行草稿";
        manager.EditSeries = fixture.Next;
        Assert.IsNull(target.ProductSeriesId);
        manager.CommitSeriesCommand.Execute(target);
        Assert.AreEqual(fixture.Next.Uid, target.ProductSeriesId);
        Assert.AreEqual(fixture.Next.DataNameZh, target.ProductSeriesName);
        Assert.IsNull(manager.EditSeries);
        manager.ClearSeriesCommand.Execute(target);
        Assert.IsNull(target.ProductSeriesId);
        Assert.AreEqual(string.Empty, target.ProductSeriesName);
        Assert.AreEqual("其他行草稿", other.DefaultDescriptionZh);
        Assert.IsTrue(other.IsDirty);
    }

    /// <summary>缓存替身只提供本地系列候选和通知；没有配置的 API 不访问网络。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly SysDictDataDto Original = SeriesItem("原系列", "FIRST");
        internal readonly SysDictDataDto Next = SeriesItem("新系列", "NEXT");
        internal SysDictDataDto[] Series;
        internal int SaveCalls;
        internal string? LastRoute;
        internal string? LastDocumentKey;
        internal DictionaryDataDocumentOpenRequest? LastRequest;
        private EventHandler? _changed;
        private readonly ReadOnlyObservableCollection<SupplierDto> _suppliers = new(new ObservableCollection<SupplierDto>());
        internal readonly IReferenceDataCacheService Cache;
        internal readonly IApiCallRunner Runner = Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke());
        internal readonly IMessageService Messages = Proxy<IMessageService>((_, _) => null);
        internal readonly ProductMasterDetailViewModel Detail;

        internal Fixture(ICurrentUserService? user = null)
        {
            Series = [Original, Next];
            Cache = Proxy<IReferenceDataCacheService>((method, args) => method.Name switch
            {
                nameof(IReferenceDataCacheService.GetDictionaryValues) => (string)args[0]! == DictCodes.ProductSeries ? Series : Array.Empty<SysDictDataDto>(),
                "get_Suppliers" => _suppliers,
                "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>()),
                "get_ProductAttributeGroups" => new ReadOnlyObservableCollection<ProductAttributeGroupDto>(new ObservableCollection<ProductAttributeGroupDto>()),
                "get_AreProductAttributesLoaded" => false,
                nameof(IReferenceDataCacheService.ResolveProductAttributes) => new ProductAttributeResolution(true, string.Empty, [], []),
                "add_ProductAttributesChanged" => Add((EventHandler)args[0]!),
                "remove_ProductAttributesChanged" => Remove((EventHandler)args[0]!),
                _ => null
            });
            var api = Proxy<IProductMasterApi>((method, _) =>
            {
                if (method.Name != nameof(IProductMasterApi.SaveDetailAsync)) throw new AssertFailedException(method.Name);
                SaveCalls++;
                return Task.FromResult(ServiceResult<SaveProductMasterDetailResponse>.Fail("VersionConflict", "测试冲突"));
            });
            var navigation = Proxy<INavigationService>((method, args) =>
            {
                Assert.AreEqual(nameof(INavigationService.OpenDocumentAsync), method.Name);
                LastRoute = (string)args[0]!;
                LastDocumentKey = (string)args[1]!;
                LastRequest = (DictionaryDataDocumentOpenRequest)args[2]!;
                return Task.CompletedTask;
            });
            Detail = new ProductMasterDetailViewModel(api, Runner, Cache, Messages, null!, navigation, new ProductMasterSyncDialogStub(), user);
            Detail.Header.Load(new ProductMasterDetailDto { Uid = Guid.NewGuid(), Version = 1, ProductSeriesName = Original.DataNameZh,
                Master = new() { MasterSku = "M", ProductSeriesId = Original.Uid } });
            Detail.Tags.Load(Guid.Empty, [], []);
            Detail.Tags.RefreshDefinitions(new ProductAttributeResolution(true, string.Empty, [], []));
            Detail.RejectDetailChangesCommand.Execute(null);
        }

        internal void PublishDictionaryChange() => _changed?.Invoke(Cache, EventArgs.Empty);
        private object? Add(EventHandler value) { _changed += value; return null; }
        private object? Remove(EventHandler value) { _changed -= value; return null; }
        public void Dispose() => Detail.Dispose();
    }

    private static SysDictDataDto SeriesItem(string name, string code) => new()
    { Uid = Guid.NewGuid(), DictCode = DictCodes.ProductSeries, DataNameZh = name, DataNameEn = code, IsEnabled = true };

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }
}

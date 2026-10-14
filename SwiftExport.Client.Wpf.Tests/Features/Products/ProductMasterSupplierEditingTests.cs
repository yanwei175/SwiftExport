using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Responses;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Suppliers.Components;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证主型号管理与详情供应商缓存身份恢复、显式提交、清空及真实本地选择控件接线。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterSupplierEditingTests
{
    /// <summary>新候选只在提交后改头部，清空不接纳其他草稿，撤销恢复原选择。</summary>
    [TestMethod]
    public void Supplier_ShouldCommitClearAndRestoreSelectionOnReject()
    {
        using var vm = CreateVm();
        var original = Detail();
        vm.Header.Load(original);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(original.Master.DefaultSupplierId, vm.SelectedSupplier!.Uid);
        var unsavedSeriesId = Guid.NewGuid();
        vm.Header.ProductSeriesId = unsavedSeriesId;
        var selected = new SupplierDto { Uid = Guid.NewGuid(), SupplierName = "新供应商" };
        vm.SelectedSupplier = selected;
        Assert.AreEqual(original.Master.DefaultSupplierId, vm.Header.DefaultSupplierId);
        vm.CommitSupplierCommand.Execute(null);
        Assert.AreEqual(selected.Uid, vm.Header.ToWriteDto().DefaultSupplierId);
        Assert.AreEqual("新供应商", vm.Header.DefaultSupplierName);
        vm.ClearSupplierCommand.Execute(null);
        Assert.AreEqual(Guid.Empty, vm.Header.DefaultSupplierId);
        Assert.AreEqual(string.Empty, vm.Header.DefaultSupplierName);
        Assert.IsNull(vm.SelectedSupplier);
        Assert.AreEqual(unsavedSeriesId, vm.Header.ProductSeriesId);
        Assert.IsNull(vm.Header.ValidateRow(), "供应商仍是可选资料。");
        Assert.IsTrue(vm.HasUnsavedChanges);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.AreEqual(original.Master.DefaultSupplierId, vm.SelectedSupplier!.Uid);
        Assert.AreEqual("原供应商", vm.SelectedSupplier.SupplierName);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>原来没有供应商时，撤销新增选择恢复为空，不留下旧控件显示对象。</summary>
    [TestMethod]
    public void Reject_ShouldRestoreEmptySupplier()
    {
        using var vm = CreateVm();
        vm.Header.Load(new ProductMasterDetailDto { Master = new() { MasterSku = "M" } });
        vm.SelectedSupplier = new SupplierDto { Uid = Guid.NewGuid(), SupplierName = "临时供应商" };
        vm.CommitSupplierCommand.Execute(null);
        vm.RejectDetailChangesCommand.Execute(null);
        Assert.IsNull(vm.SelectedSupplier);
        Assert.AreEqual(Guid.Empty, vm.Header.DefaultSupplierId);
        Assert.AreEqual(string.Empty, vm.Header.DefaultSupplierName);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>读取和保存回填同步选择；保存失败不清除本地供应商编辑。</summary>
    [TestMethod]
    public async Task LoadAndSave_ShouldSynchronizeSelectionOnlyOnSuccess()
    {
        var detail = Detail();
        var failSave = true;
        var api = Proxy<IProductMasterApi>((method, _) => method.Name switch
        {
            nameof(IProductMasterApi.GetDetailAsync) => Task.FromResult(ServiceResult<ProductMasterDetailDto>.Ok(detail)),
            nameof(IProductMasterApi.SaveDetailAsync) => Task.FromResult(failSave
                ? ServiceResult<SaveProductMasterDetailResponse>.Fail("保存失败")
                : ServiceResult<SaveProductMasterDetailResponse>.Ok(new() { Detail = detail })),
            _ => throw new AssertFailedException(method.Name)
        });
        using var vm = CreateVm(api);
        vm.Header.Load(detail);
        await vm.RefreshCommand.ExecuteAsync(null);
        Assert.AreEqual("原供应商", vm.SelectedSupplier!.SupplierName);
        var selected = new SupplierDto { Uid = Guid.NewGuid(), SupplierName = "本地修改" };
        vm.SelectedSupplier = selected;
        vm.CommitSupplierCommand.Execute(null);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreSame(selected, vm.SelectedSupplier);
        Assert.AreEqual(selected.Uid, vm.Header.DefaultSupplierId);
        Assert.IsTrue(vm.HasUnsavedChanges);
        failSave = false;
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(detail.Master.DefaultSupplierId, vm.SelectedSupplier!.Uid);
        Assert.AreEqual("原供应商", vm.SelectedSupplier.SupplierName);
        Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>真实供应商控件绑定完整缓存，搜索不写业务字段，键盘确认和清空写回原供应商字段。</summary>
    [TestMethod]
    public void CachedSupplierEditor_ShouldSearchLocallyAndCommitOnlyConfirmedCandidate()
    {
        StaTest.RunAsync(async () =>
        {
            var original = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-001", SupplierName = "原供应商" };
            var next = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-002", SupplierName = "新供应商" };
            using var vm = CreateVm(suppliers: [original, next]);
            vm.Header.Load(new ProductMasterDetailDto { Uid = Guid.NewGuid(), Version = 1,
                DefaultSupplierName = original.SupplierName, Master = new() { MasterSku = "M", DefaultSupplierId = original.Uid } });
            vm.RejectDetailChangesCommand.Execute(null);
            Assert.AreSame(original, vm.SelectedSupplier, "回填按 Uid 匹配完整缓存对象，供应商编码不能丢失。");
            var lookup = new SupplierAutoSuggestComboBox { DataContext = vm };
            lookup.SetBinding(SupplierAutoSuggestComboBox.ItemsSourceProperty, new Binding(nameof(vm.SupplierOptions)));
            lookup.SetBinding(SupplierAutoSuggestComboBox.SelectedSupplierProperty,
                new Binding(nameof(vm.SelectedSupplier)) { Mode = BindingMode.TwoWay });
            lookup.SetBinding(SupplierAutoSuggestComboBox.SelectionCommittedCommandProperty, new Binding(nameof(vm.CommitSupplierCommand)));
            lookup.SetBinding(SupplierAutoSuggestComboBox.SelectionClearedCommandProperty, new Binding(nameof(vm.ClearSupplierCommand)));
            var window = new Window { Content = lookup, Width = 460, Height = 180, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            try
            {
                window.Show(); window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = (LookupTextBox)lookup.FindName("LookupEditor");
                Assert.AreEqual(LookupTextBoxSearchMode.Local, editor.SearchMode);
                Assert.IsTrue(editor.ShowDropDownButton);
                Assert.IsTrue(editor.RequireItemMatch);
                Assert.AreSame(original, editor.SelectedItem);
                editor.SearchDelay = TimeSpan.FromMilliseconds(20);
                editor.FocusEditor();
                await Dispatcher.Yield(DispatcherPriority.Input);
                var input = (TextBox)editor.FindName("InputTextBox");
                input.Text = "SUP-002";
                await Task.Delay(80);
                var list = (ListBox)editor.FindName("ResultList");
                Assert.AreEqual(1, list.Items.Count);
                Assert.AreSame(next, list.Items[0]);
                Assert.AreEqual(original.Uid, vm.Header.DefaultSupplierId);
                Assert.IsFalse(vm.HasUnsavedChanges);
                list.SelectedItem = next;
                input.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(input)!,
                    Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                Assert.AreSame(next, vm.SelectedSupplier);
                Assert.AreEqual(next.Uid, vm.Header.ToWriteDto().DefaultSupplierId);
                Assert.AreEqual(next.SupplierName, vm.Header.DefaultSupplierName);
                ((Button)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.AreEqual(Guid.Empty, vm.Header.DefaultSupplierId);
                Assert.IsNull(vm.SelectedSupplier);
                vm.RejectDetailChangesCommand.Execute(null);
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert.AreSame(original, editor.SelectedItem);
                Assert.IsFalse(vm.HasUnsavedChanges);
            }
            finally { window.Close(); }
        });
    }

    /// <summary>编辑态从会话缓存恢复当前供应商；确认和清空只修改目标行，其他字段及其他行草稿保留。</summary>
    [TestMethod]
    public void ManagerSupplier_ShouldRestoreCacheCandidateAndChangeOnlyTargetRow()
    {
        var original = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-001", SupplierName = "原供应商" };
        var next = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-002", SupplierName = "新供应商" };
        using var vm = CreateManager(suppliers: [original, next]);
        vm.Masters.LoadFromDtos([
            new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, DefaultSupplierName = original.SupplierName,
                Data = new() { MasterSku = "A", DefaultSupplierId = original.Uid } },
            new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1, DefaultSupplierName = "其他供应商",
                Data = new() { MasterSku = "B", DefaultSupplierId = Guid.NewGuid() } }]);
        var target = vm.Masters.Items[0];
        var other = vm.Masters.Items[1];
        var otherSupplierId = other.DefaultSupplierId;
        vm.PrepareSupplierEditor(target);
        Assert.AreSame(original, vm.SupplierOptions[0]);
        Assert.AreSame(original, vm.EditSupplier);
        Assert.IsFalse(target.IsDirty, "打开本地候选不能提前改业务身份或产生草稿。");
        target.DefaultDescriptionZh = "目标其他草稿";
        other.DefaultDescriptionZh = "其他行草稿";
        vm.EditSupplier = next;
        Assert.AreEqual(original.Uid, target.DefaultSupplierId);
        vm.CommitSupplierCommand.Execute(target);
        Assert.AreEqual(next.Uid, target.ToWriteDto().DefaultSupplierId);
        Assert.AreEqual(next.SupplierName, target.DefaultSupplierName);
        Assert.AreSame(next, vm.EditSupplier, "双向选择保留当前项，单元格中仍能显示已确认供应商并继续清空。");
        vm.ClearSupplierCommand.Execute(target);
        Assert.IsNull(vm.EditSupplier);
        Assert.AreEqual(Guid.Empty, target.DefaultSupplierId);
        Assert.AreEqual(string.Empty, target.DefaultSupplierName);
        Assert.AreEqual("目标其他草稿", target.DefaultDescriptionZh);
        Assert.AreEqual("其他行草稿", other.DefaultDescriptionZh);
        Assert.AreEqual(otherSupplierId, other.DefaultSupplierId);
        Assert.IsTrue(other.IsDirty);
        target.RejectChanges();
        vm.PrepareSupplierEditor(target);
        Assert.AreSame(original, vm.EditSupplier);
        Assert.IsFalse(target.IsDirty);
        Assert.IsTrue(other.IsDirty);
    }

    /// <summary>保存等待、查询及只读状态拒绝供应商确认和清空，失败回执不丢失原草稿。</summary>
    [TestMethod]
    public async Task ManagerSupplier_ShouldRejectChangesWhileSavingQueryingOrReadOnly()
    {
        var original = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-001", SupplierName = "原供应商" };
        var next = new SupplierDto { Uid = Guid.NewGuid(), SupplierCode = "SUP-002", SupplierName = "新供应商" };
        var completion = new TaskCompletionSource<ServiceResult<ProductMasterBatchSaveResponse>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var api = Proxy<IProductMasterApi>((method, _) => method.Name == nameof(IProductMasterApi.BatchSaveAsync)
            ? completion.Task : throw new AssertFailedException(method.Name));
        using var vm = CreateManager(api, [original, next]);
        vm.Masters.LoadFromDtos([new ProductMasterListItemDto { Uid = Guid.NewGuid(), Version = 1,
            DefaultSupplierName = original.SupplierName, Data = new() { MasterSku = "A", DefaultSupplierId = original.Uid } }]);
        var row = vm.Masters.Items.Single();
        row.DefaultDescriptionZh = "未保存草稿";
        var saving = vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy);
        vm.EditSupplier = next;
        vm.CommitSupplierCommand.Execute(row);
        vm.ClearSupplierCommand.Execute(row);
        Assert.AreEqual(original.Uid, row.DefaultSupplierId);
        completion.SetResult(ServiceResult<ProductMasterBatchSaveResponse>.Fail("测试保存失败"));
        await saving;
        Assert.IsFalse(vm.IsBusy);
        Assert.AreEqual("未保存草稿", row.DefaultDescriptionZh);
        Assert.IsTrue(row.IsDirty);
        vm.IsQuerying = true;
        vm.EditSupplier = next;
        vm.CommitSupplierCommand.Execute(row);
        vm.ClearSupplierCommand.Execute(row);
        Assert.AreEqual(original.Uid, row.DefaultSupplierId);
        vm.IsQuerying = false;
        // 工作区负责将页面置为只读，测试通过同一公开属性模拟该已解析导航状态。
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(vm, true);
        Assert.IsFalse(vm.CanWrite);
        vm.EditSupplier = next;
        vm.CommitSupplierCommand.Execute(row);
        vm.ClearSupplierCommand.Execute(row);
        Assert.AreEqual(original.Uid, row.DefaultSupplierId);
        Assert.AreEqual(original.SupplierName, row.DefaultSupplierName);
        Assert.IsTrue(row.IsDirty);
    }

    /// <summary>管理页依赖只提供本地供应商缓存和受控保存接口，不存在远程供应商搜索依赖。</summary>
    private static ProductMasterManagerViewModel CreateManager(IProductMasterApi? api = null, SupplierDto[]? suppliers = null)
    {
        var supplierCache = new ReadOnlyObservableCollection<SupplierDto>(new ObservableCollection<SupplierDto>(suppliers ?? []));
        var cache = Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
        {
            "get_Suppliers" => supplierCache,
            nameof(IReferenceDataCacheService.GetDictionaryValues) => Array.Empty<SysDictDataDto>(),
            _ => null
        });
        return new ProductMasterManagerViewModel(api!, Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()),
            cache, Proxy<IMessageService>((_, _) => null), null!, null!, null!, new ImageAddressResolverStub());
    }

    private static ProductMasterDetailDto Detail() => new()
    {
        Uid = Guid.NewGuid(), Version = 1, DefaultSupplierName = "原供应商",
        Master = new() { MasterSku = "M", DefaultSupplierId = Guid.NewGuid() }
    };

    /// <summary>提供稳定的只读供应商集合，允许详情订阅与释放缓存变化，不请求远程供应商接口。</summary>
    private static ProductMasterDetailViewModel CreateVm(IProductMasterApi? api = null, SupplierDto[]? suppliers = null)
    {
        var supplierCache = new ReadOnlyObservableCollection<SupplierDto>(new ObservableCollection<SupplierDto>(suppliers ?? []));
        return new(
        api!, Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()),
        Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
        {
            "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>()),
            "get_Suppliers" => supplierCache,
            nameof(IReferenceDataCacheService.GetDictionaryValues) => Array.Empty<SysDictDataDto>(),
            "get_ProductAttributeGroups" => new ReadOnlyObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto>(new ObservableCollection<SwiftExport.Contracts.DTOs.Products.Attributes.ProductAttributeGroupDto>()),
            "get_AreProductAttributesLoaded" => false,
                nameof(IReferenceDataCacheService.ResolveProductAttributes) => new ProductAttributeResolution(true, string.Empty, [], []),
            "add_ProductAttributesChanged" or "remove_ProductAttributesChanged" => null,
            _ => throw new AssertFailedException(method.Name)
        }),
        Proxy<IMessageService>((_, _) => null), null!, null!);
    }

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> invoke) where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).InvokeMethod = invoke;
        return proxy;
    }

    /// <summary>仅提供当前测试需要的依赖，禁止隐式请求网络或弹窗。</summary>
    public class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[], object?> InvokeMethod { get; set; } = null!;
        /// <summary>转发受控调用，未配置的方法由测试直接拒绝。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => InvokeMethod(targetMethod!, args ?? []);
    }
}

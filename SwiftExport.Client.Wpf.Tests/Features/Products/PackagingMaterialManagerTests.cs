using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.PackagingMaterials.ViewModels;
using SwiftExportClient.Features.Products.PackagingMaterials.Views;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Services;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Navigation;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>包材专页的固定类型、冻结请求、版本、权限、关闭与真实原生编辑桥接；全部使用离线 API。</summary>
[TestClass, DoNotParallelize]
public sealed class PackagingMaterialManagerTests
{
    /// <summary>搜索框的 Enter 在页面初始化及数据上下文替换后执行当前查询，且不产生无管理元素的命令绑定错误。</summary>
    [TestMethod]
    public void SearchEnter_TracksCurrentPageWithoutBindingErrors() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        PresentationTraceSources.Refresh();
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        var trace = PresentationTraceSources.DataBindingSource;
        var previousLevel = trace.Switch.Level;
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(listener);
        using var first = new Fixture(); using var second = new Fixture();
        var window = new Window { Width = 1000, Height = 480, ShowInTaskbar = false };
        try
        {
            var view = new PackagingMaterialManagerView(); window.Content = view; window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            foreach (var fixture in new[] { first, second })
            {
                view.DataContext = fixture.ViewModel;
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                var search = Descendants(view).OfType<TextBox>().Single(item => System.Windows.Automation.AutomationProperties.GetName(item) == "包材搜索");
                var enter = search.InputBindings.OfType<KeyBinding>().Single(item => item.Key == Key.Enter);
                Assert.AreSame(fixture.ViewModel.SearchCommand, enter.Command);
                search.Text = fixture == first ? "第一组包材" : "第二组包材";
                search.Focus();
                search.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(search)!, Environment.TickCount, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent });
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                Assert.IsNotNull(fixture.LastSearch);
                Assert.AreEqual(search.Text, fixture.LastSearch.Keyword);
            }
            Assert.AreEqual("第一组包材", first.LastSearch!.Keyword);
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            listener.Flush();
            Assert.IsFalse(output.ToString().Contains("SearchCommand", StringComparison.Ordinal), output.ToString());
        }
        finally
        {
            window.Close();
            trace.Listeners.Remove(listener); trace.Switch.Level = previousLevel;
        }
    });

    /// <summary>查询与重置始终固定包材，新增以同一 Product 批量协议保存，乱序回执仍按 ClientKey 接纳。</summary>
    [TestMethod]
    public async Task SearchResetAndBatchSave_KeepPackagingIdentityAndReceipts()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Keyword = "规格";
        await vm.SearchCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { MaterialTypesEnum.包材 }, fixture.LastSearch!.MaterialTypeList);
        Assert.AreEqual("规格", fixture.LastSearch.Keyword);
        await vm.ResetCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { MaterialTypesEnum.包材 }, fixture.LastSearch!.MaterialTypeList);
        Assert.AreEqual("", vm.Keyword);
        vm.AddRowCommand.Execute(null); var first = vm.SelectedLine!; first.Sku = "PACK-1"; first.UnitWeight = 0.2m;
        vm.AddRowCommand.Execute(null); var second = vm.SelectedLine!; second.Sku = "PACK-2";
        Assert.AreEqual(MaterialTypesEnum.包材, first.MaterialType);
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(2, fixture.LastSave!.CreateItems.Count);
        Assert.AreEqual(0.2m, fixture.LastSave.CreateItems[0].Data.UnitWeight);
        Assert.IsTrue(fixture.LastSave.CreateItems.All(item => item.Data.UpdateUnitWeight));
        Assert.IsTrue(vm.Products.Items.All(row => !row.IsNew && !row.IsDirty && row.Version == 1 && row.Uid != Guid.Empty));
        Assert.AreNotEqual(first.Uid, second.Uid);
        first.Note = "修改";
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(2, first.Version); Assert.IsFalse(first.IsDirty);
        first.Note = "撤销"; vm.RejectCommand.Execute(null);
        Assert.AreEqual("修改", first.Note);
        vm.SelectedLine = first; await vm.DeleteSelectedCommand.ExecuteAsync(null);
        Assert.AreEqual(1, vm.Products.DeletedItems.Count);
        vm.RejectCommand.Execute(null); Assert.AreEqual(2, vm.Products.Items.Count);
        vm.SelectedLine = first; await vm.DeleteSelectedCommand.ExecuteAsync(null); await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(1, vm.Products.Items.Count); Assert.IsFalse(vm.HasUnsavedChanges);
    }

    /// <summary>失败、版本冲突和不完整回执不接纳版本与基线，也不覆盖单元格新输入。</summary>
    [TestMethod]
    [DataRow("VERSION_CONFLICT")]
    [DataRow("FORBIDDEN")]
    [DataRow("INCOMPLETE")]
    public async Task SaveFailure_KeepsDraftAndExpectedVersion(string failure)
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Products.LoadFromDtos([Row()]); var row = vm.Products.Items.Single(); row.Note = "未提交备注";
        fixture.Save = _ => Task.FromResult(failure == "INCOMPLETE"
            ? ServiceResult<ProductBatchSaveResponse>.Ok(new())
            : ServiceResult<ProductBatchSaveResponse>.Fail(failure, "测试失败"));
        await vm.SaveCommand.ExecuteAsync(null);
        Assert.AreEqual(7, row.Version); Assert.IsTrue(row.IsDirty); Assert.AreEqual("未提交备注", row.Note);
        Assert.IsTrue(vm.HasUnsavedChanges);
    }

    /// <summary>原生编辑提交失败阻止保存；显式放弃被取消时不翻页、不查询也不丢失草稿。</summary>
    [TestMethod]
    public async Task PendingCellAndDiscardCancellation_BlockContextReplacement()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Products.LoadFromDtos([Row()]); vm.CommitPendingEdits = () => false;
        await vm.SaveCommand.ExecuteAsync(null); Assert.IsNull(fixture.LastSave);
        vm.CommitPendingEdits = () => true; vm.Products.Items.Single().Note = "保留"; fixture.Confirm = false;
        await vm.PageChangeCommand.ExecuteAsync(new() { NewIndex = 3, NewSize = 50 });
        Assert.IsNull(fixture.LastSearch); Assert.AreEqual(1, vm.PageIndex); Assert.AreEqual("保留", vm.Products.Items.Single().Note);
        Assert.IsFalse(await vm.CanCloseAsync(default));
    }

    /// <summary>保存使用冻结快照；发送期间的新本地修改保留为草稿，Busy 期间拒绝关闭。</summary>
    [TestMethod]
    public async Task InFlightSave_PreservesNewerDraftAndBlocksClose()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Products.LoadFromDtos([Row()]); var row = vm.Products.Items.Single(); row.Note = "发送值";
        var pending = new TaskCompletionSource<ServiceResult<ProductBatchSaveResponse>>();
        fixture.Save = _ => pending.Task;
        var operation = vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(vm.IsBusy); Assert.IsFalse(await vm.CanCloseAsync(default));
        row.Note = "较新草稿";
        Assert.AreEqual("发送值", fixture.LastSave!.UpdateItems.Single().Data.Note);
        pending.SetResult(Receipt(fixture.LastSave)); await operation;
        Assert.AreEqual(8, row.Version); Assert.AreEqual("较新草稿", row.Note); Assert.IsTrue(row.IsDirty);
        vm.RejectCommand.Execute(null); Assert.AreEqual("发送值", row.Note);
    }

    /// <summary>只读不能创建或编辑；创建与修改分权，单元格选择不允许删除行或打开详情。</summary>
    [TestMethod]
    public async Task PermissionsAndCellSelection_BlockRowOperations()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Products.LoadFromDtos([Row()]); var row = vm.Products.Items.Single(); vm.SelectedLine = row; row.IsChecked = true;
        vm.IsCellSelectionMode = true;
        Assert.IsFalse(vm.DeleteSelectedCommand.CanExecute(null)); Assert.IsFalse(vm.OpenDetailCommand.CanExecute(null));
        await vm.DeleteSelectedCommand.ExecuteAsync(null); Assert.AreEqual(1, vm.Products.Items.Count);
        fixture.SetPermissions(PermissionKeys.ProductsProductView);
        Assert.IsTrue(vm.IsGridReadOnly); Assert.IsFalse(vm.CanEditRow(row)); Assert.IsFalse(vm.AddRowCommand.CanExecute(null)); Assert.IsFalse(vm.SaveCommand.CanExecute(null));
        fixture.SetPermissions(PermissionKeys.ProductsProductView, PermissionKeys.ProductsProductCreate);
        Assert.IsFalse(vm.CanEditRow(row)); vm.AddRowCommand.Execute(null); Assert.IsTrue(vm.CanEditRow(vm.SelectedLine!));
    }

    /// <summary>详情复用 Product 文档身份，确认放弃后不会把列表草稿带入详情。</summary>
    [TestMethod]
    public async Task DetailNavigation_ReusesProductIdentityAndRejectsConfirmedDraft()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Products.LoadFromDtos([Row()]); var row = vm.Products.Items.Single(); vm.SelectedLine = row; row.Note = "草稿";
        await vm.OpenDetailCommand.ExecuteAsync(null);
        Assert.AreEqual($"product:{row.Uid:N}", fixture.DocumentKey);
        Assert.IsFalse(vm.HasUnsavedChanges); Assert.AreEqual("原备注", row.Note);
    }

    /// <summary>详情保存消息刷新干净列表，有本地草稿时只提示，供应商身份与显示值成组维护。</summary>
    [TestMethod]
    public async Task DetailMessageAndSupplierSelection_RespectDraftOwnership()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        var data = Row(); vm.Products.LoadFromDtos([data]);
        fixture.Read = request => Task.FromResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new()
        { Items = [new ProductDto { Uid = data.Uid, Version = 8, Sku = "更新后", MaterialType = MaterialTypesEnum.包材 }], PageIndex = 1, PageSize = 20 }));
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(data.Uid));
        var row = vm.Products.Items.Single(); Assert.AreEqual("更新后", row.Sku); Assert.AreEqual(8, row.Version);
        vm.EditSupplier = new() { Uid = Guid.NewGuid(), SupplierCode = "SUP", SupplierName = "供应商" };
        vm.CommitSupplierCommand.Execute(row);
        Assert.AreEqual(vm.EditSupplier.Uid, row.DefaultSupplierId); Assert.AreEqual("SUP", row.DefaultSupplierCode); Assert.AreEqual("供应商", row.SupplierName);
        fixture.LastSearch = null;
        WeakReferenceMessenger.Default.Send(new ProductDetailSavedMessage(data.Uid));
        Assert.IsNull(fixture.LastSearch); Assert.IsTrue(row.IsDirty);
        vm.ClearSupplierCommand.Execute(row); Assert.IsNull(row.DefaultSupplierId); Assert.AreEqual("", row.SupplierName);
        await Task.CompletedTask;
    }

    /// <summary>释放期间取消正在飞行的分页，忽略不遵守取消令牌的旧响应。</summary>
    [TestMethod]
    public async Task DisposedQuery_DoesNotReplaceList()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        var pending = new TaskCompletionSource<ServiceResult<PagedResponseBase<ProductDto>>>();
        fixture.Read = _ => pending.Task;
        var operation = vm.SearchCommand.ExecuteAsync(null); vm.Dispose();
        Assert.IsTrue(fixture.QueryToken.IsCancellationRequested);
        pending.SetResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new() { Items = [Row()], PageIndex = 1, PageSize = 20 }));
        await operation; Assert.AreEqual(0, vm.Products.Items.Count);
    }

    /// <summary>快速连续查询取消旧令牌，旧响应晚到既不覆盖新结果，也不结束较新的读取状态。</summary>
    [TestMethod]
    public async Task NewerQuery_WinsEvenWhenOldApiIgnoresCancellation()
    {
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        var first = new TaskCompletionSource<ServiceResult<PagedResponseBase<ProductDto>>>();
        var second = new TaskCompletionSource<ServiceResult<PagedResponseBase<ProductDto>>>();
        fixture.Read = request => request.Keyword == "旧" ? first.Task : second.Task;
        vm.Keyword = "旧"; var old = vm.SearchCommand.ExecuteAsync(null); var oldToken = fixture.QueryToken;
        vm.Keyword = "新"; var current = vm.SearchCommand.ExecuteAsync(null);
        Assert.IsTrue(oldToken.IsCancellationRequested);
        first.SetResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new() { Items = [Row()], PageIndex = 1, PageSize = 20 }));
        await old; Assert.IsTrue(vm.IsQueryBusy); Assert.AreEqual(0, vm.Products.Items.Count);
        var newest = Row(); newest.Sku = "新结果";
        second.SetResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new() { Items = [newest], PageIndex = 1, PageSize = 20, TotalCount = 1 }));
        await current; Assert.AreEqual("新结果", vm.Products.Items.Single().Sku); Assert.IsFalse(vm.IsQueryBusy);
    }

    /// <summary>真实 WPF 表格中尚未提交的文本通过 View 编辑桥进入冻结请求；卸载时桥被解除。</summary>
    [TestMethod]
    public void NativeGrid_PendingCellIsCommittedBeforeSave() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture(); var vm = fixture.ViewModel;
        vm.Products.LoadFromDtos([Row()]);
        var view = new PackagingMaterialManagerView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 500, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName("PackageGrid"); var row = vm.Products.Items.Single();
            grid.SelectedItem = row; grid.CurrentCell = new DataGridCellInfo(row, grid.Columns[2]);
            Assert.IsTrue(grid.BeginEdit()); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var editor = Descendants(grid.Columns[2].GetCellContent(row)).OfType<TextBox>().First();
            // 将当前编辑切成失焦提交，证明保存桥并不依赖逐键写回模型。
            var binding = new Binding(nameof(ProductLineModel.Sku)) { Mode = BindingMode.TwoWay, UpdateSourceTrigger = UpdateSourceTrigger.LostFocus };
            editor.SetBinding(TextBox.TextProperty, binding); editor.Text = "BUFFERED-PACK";
            Assert.AreNotEqual("BUFFERED-PACK", row.Sku);
            await vm.SaveCommand.ExecuteAsync(null);
            Assert.AreEqual("BUFFERED-PACK", fixture.LastSave!.UpdateItems.Single().Data.Sku);
            Assert.IsFalse(row.IsDirty); Assert.AreEqual(8, row.Version);
            window.Content = null; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsNull(vm.CommitPendingEdits);
        }
        finally { window.Close(); }
    });

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    { yield return root; for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child; }
    private static ProductDto Row() => new() { Uid = Guid.NewGuid(), Version = 7, Sku = "PACK", MaterialType = MaterialTypesEnum.包材, SupplyType = SupplyTypeEnums.采购, Note = "原备注", UnitWeight = 0.3m };
    /// <summary>按真实服务端回执字段构造乱序结果，用 ClientKey 而非返回顺序关联。</summary>
    private static ServiceResult<ProductBatchSaveResponse> Receipt(ProductBatchSaveRequest request) => ServiceResult<ProductBatchSaveResponse>.Ok(new()
    {
        Items = request.CreateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Operation = SaveOperation.Create, Uid = Guid.NewGuid(), NewVersion = 1, Success = true })
            .Concat(request.UpdateItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Operation = SaveOperation.Update, Uid = item.Uid, NewVersion = item.Version + 1, Success = true }))
            .Concat(request.DeleteItems.Select(item => new SaveItemResult { ClientKey = item.ClientKey, Operation = SaveOperation.Delete, Uid = item.Uid, NewVersion = item.Version + 1, Success = true })).Reverse().ToList()
    });
    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    { var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>(); ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call; return proxy; }

    /// <summary>只模拟 API、权限和确认结果，列表状态及请求构造全部使用真实实现。</summary>
    private sealed class Fixture : IDisposable
    {
        public readonly CurrentUserService User = new();
        public PackagingMaterialManagerViewModel ViewModel { get; }
        public ProductSearchRequest? LastSearch;
        public ProductBatchSaveRequest? LastSave;
        public string? DocumentKey;
        public CancellationToken QueryToken;
        public bool Confirm = true;
        public Func<ProductBatchSaveRequest, Task<ServiceResult<ProductBatchSaveResponse>>> Save = request => Task.FromResult(Receipt(request));
        public Func<ProductSearchRequest, Task<ServiceResult<PagedResponseBase<ProductDto>>>> Read = request => Task.FromResult(ServiceResult<PagedResponseBase<ProductDto>>.Ok(new() { PageIndex = request.PageIndex, PageSize = request.PageSize, Items = [] }));
        public Fixture()
        {
            SetPermissions(PermissionKeys.ProductsProductView, PermissionKeys.ProductsProductCreate, PermissionKeys.ProductsProductEdit, PermissionKeys.ProductsProductDelete);
            var api = Proxy<IProductApi>((method, args) => method.Name switch
            {
                nameof(IProductApi.GetPagedAsync) => ReadPage((ProductSearchRequest)args[0]!, (CancellationToken)args[1]!),
                nameof(IProductApi.BatchSaveAsync) => SaveBatch((ProductBatchSaveRequest)args[0]!), _ => throw new NotSupportedException(method.Name)
            });
            ViewModel = new(api, Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()),
                Proxy<IDialogService>((_, _) => Task.FromResult(Confirm)), User,
                Proxy<INavigationService>((method, args) => { if (method.Name == nameof(INavigationService.OpenDocumentAsync)) DocumentKey = (string)args[1]!; return Task.CompletedTask; }),
                new ReferenceDataCacheServiceStub(), Proxy<IProductImageUploadDialogService>((_, _) => Task.CompletedTask));
        }
        public void SetPermissions(params string[] permissions) => User.SetCurrentUser(new MeDto { Permissions = permissions.ToList() });
        private Task<ServiceResult<PagedResponseBase<ProductDto>>> ReadPage(ProductSearchRequest request, CancellationToken token)
        { LastSearch = request; QueryToken = token; return Read(request); }
        private Task<ServiceResult<ProductBatchSaveResponse>> SaveBatch(ProductBatchSaveRequest request) { LastSave = request; return Save(request); }
        public void Dispose() => ViewModel.Dispose();
    }
}

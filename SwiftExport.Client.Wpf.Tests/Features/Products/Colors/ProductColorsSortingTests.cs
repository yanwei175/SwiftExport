using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using GongSolutions.Wpf.DragDrop;
using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Features.Products.Colors.ViewModels;
using SwiftExportClient.Features.Products.Colors.Models;
using SwiftExportClient.Features.Products.Colors.Views;
using SwiftExportClient.Mvvm;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using GongDragDrop = GongSolutions.Wpf.DragDrop.DragDrop;

namespace SwiftExport.Client.Wpf.Tests.Features.Products.Colors;

/// <summary>验证全量颜色编辑、排序门禁、批量保存及实际高级表格接线，不访问真实服务。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductColorsSortingTests
{
    /// <summary>上移下移同步业务排序号与脏状态，撤销同时恢复字段和集合顺序。</summary>
    [TestMethod]
    public void MovesRenumberAndRejectRestoresBaseline() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture();
        await f.Vm.InitializeAsync();
        var original = f.Vm.Lines.Items.ToArray();
        Assert.IsFalse(f.Vm.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(f.Vm.Lines.Items, original[0], -1)));
        Assert.IsFalse(f.Vm.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(f.Vm.Lines.Items, original[2], 1)));
        f.Vm.ReorderLineCommand.Execute(RowReorderTestRequests.Step(f.Vm.Lines.Items, original[0], 1));
        CollectionAssert.AreEqual(new[] { original[1], original[0], original[2] }, f.Vm.Lines.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, f.Vm.Lines.Items.Select(line => line.SortNo).ToArray());
        Assert.AreSame(original[0], f.Vm.SelectedLine);
        Assert.IsTrue(f.Vm.HasChanges);
        Assert.IsTrue(f.Vm.SaveCommand.CanExecute(null));
        f.Vm.RejectAllChangesCommand.Execute(null);
        CollectionAssert.AreEqual(original, f.Vm.Lines.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, original.Select(line => line.SortNo).ToArray());
        Assert.IsFalse(f.Vm.HasChanges);
        f.Vm.ReorderLineCommand.Execute(RowReorderTestRequests.Step(f.Vm.Lines.Items, original[2], -1));
        Assert.AreSame(original[2], f.Vm.Lines.Items[1]);
    });

    /// <summary>本地搜索四类字段，不丢弃编辑或请求服务；清空搜索恢复移动及空视图提示。</summary>
    [TestMethod]
    [DataRow("unique-b")] [DataRow("BLUE")] [DataRow("deep-blue")] [DataRow("蓝色")]
    public void SearchIsLocalAndBlocksReorder(string keyword) => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture();
        await f.Vm.InitializeAsync();
        var first = f.Vm.Lines.Items[0];
        first.ColorNameZh = "未保存编辑";
        f.Vm.Keyword = keyword;
        Assert.AreEqual("UNIQUE-B", f.Vm.ColorsView.Cast<SwiftExportClient.Features.Products.Colors.Models.ProductColorLineModel>().Single().ColorCodeUnique);
        Assert.IsFalse(f.Vm.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1)));
        f.Vm.ReorderLineCommand.Execute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1));
        Assert.AreSame(first, f.Vm.Lines.Items[0]);
        f.Vm.SearchCommand.Execute(null);
        Assert.AreEqual(1, f.Loads);
        Assert.AreEqual(0, f.Confirms);
        f.Vm.Keyword = "找不到的颜色";
        Assert.IsTrue(f.Vm.IsEmpty);
        f.Vm.Keyword = string.Empty;
        Assert.IsNull(f.Vm.ColorsView.Filter);
        Assert.IsTrue(f.Vm.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1)));
        Assert.AreEqual("未保存编辑", first.ColorNameZh);
        Assert.IsTrue(f.Vm.HasChanges);
    });

    /// <summary>新增使用完整列表最大排序号，删除不重编号，负排序号不能通过本地校验。</summary>
    [TestMethod]
    public void AddAppendsAndDeleteKeepsOtherSortNumbers() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture();
        await f.Vm.InitializeAsync();
        f.Vm.Keyword = "蓝色";
        f.Vm.AddRowCommand.Execute(null);
        Assert.AreEqual(string.Empty, f.Vm.Keyword);
        Assert.AreEqual(31, f.Vm.Lines.Items.Last().SortNo);
        Assert.AreSame(f.Vm.Lines.Items.Last(), f.Vm.SelectedLine);
        await f.Vm.DeleteSelectedCommand.ExecuteAsync(null);
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, f.Vm.Lines.Items.Select(line => line.SortNo).ToArray());
        var line = f.Vm.Lines.Items[0];
        line.SortNo = -1;
        Assert.IsNotNull(await line.ValidateAsync());
    });

    /// <summary>失败保留完整排序草稿，成功提交 Uid/Version 后重载并刷新缓存，缓存失败明确提示。</summary>
    [TestMethod]
    public void SaveFailurePreservesEditsAndSuccessRefreshesCache() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture();
        await f.Vm.InitializeAsync();
        var first = f.Vm.Lines.Items[0];
        f.Vm.ReorderLineCommand.Execute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1));
        f.SaveSuccess = false;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.HasChanges);
        Assert.AreSame(first, f.Vm.Lines.Items[1]);
        Assert.AreEqual(0, f.CacheRefreshes);
        Assert.IsNotNull(f.LastSave);
        var change = f.LastSave.UpdateItems.Single(item => item.Uid == first.Uid);
        Assert.AreEqual(2, change.Data.SortNo);
        Assert.AreEqual(4, change.Version);
        f.SaveSuccess = true;
        f.CacheSuccess = false;
        await f.Vm.SaveCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.HasChanges);
        Assert.AreEqual(2, f.Loads);
        Assert.AreEqual(1, f.CacheRefreshes);
        Assert.IsTrue(f.Messages.Any(text => text.Contains("已保存，但缓存刷新失败", StringComparison.Ordinal)));
    });

    /// <summary>只读和等待中的请求均拒绝移动，页面释放后迟到读取不能覆盖原列表。</summary>
    [TestMethod]
    public void ReadOnlyBusyAndDisposedPageRejectReorderOrLateLoad() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture();
        await f.Vm.InitializeAsync();
        var first = f.Vm.Lines.Items[0];
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(f.Vm, true);
        Assert.IsFalse(f.Vm.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1)));
        f.Vm.ReorderLineCommand.Execute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1));
        Assert.AreSame(first, f.Vm.Lines.Items[0]);
        typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(f.Vm, false);
        f.LoadGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsTrue(f.Vm.IsBusy);
        Assert.IsFalse(f.Vm.ReorderLineCommand.CanExecute(RowReorderTestRequests.Step(f.Vm.Lines.Items, first, 1)));
        f.Vm.Dispose();
        f.LoadGate.SetResult(ServiceResult<List<ProductColorDto>>.Ok([]));
        await pending;
        Assert.AreSame(first, f.Vm.Lines.Items[0]);
    });

    /// <summary>跨行拖动使用最终索引，拒绝越界、原地及过期模型，撤销恢复完整基线。</summary>
    [TestMethod]
    public void DragDestinationPreservesIdentityAndRejectsInvalidMoves() => StaTest.RunAsync(async () =>
    {
        using var f = new Fixture();
        await f.Vm.InitializeAsync();
        var original = f.Vm.Lines.Items.ToArray();
        Assert.IsFalse(f.Vm.MoveLineTo(original[0], 0));
        Assert.IsFalse(f.Vm.MoveLineTo(original[0], -1));
        Assert.IsFalse(f.Vm.MoveLineTo(original[0], 3));
        Assert.IsFalse(f.Vm.MoveLineTo(new ProductColorLineModel(), 1));
        Assert.IsFalse(f.Vm.HasChanges);
        Assert.IsTrue(f.Vm.MoveLineTo(original[0], 2));
        CollectionAssert.AreEqual(new[] { original[1], original[2], original[0] }, f.Vm.Lines.Items.ToArray());
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, f.Vm.Lines.Items.Select(line => line.SortNo).ToArray());
        Assert.IsTrue(f.Vm.MoveLineTo(original[0], 0));
        CollectionAssert.AreEqual(original, f.Vm.Lines.Items.ToArray());
        Assert.IsTrue(original.All(line => line.Version == 4));
        f.Vm.RejectAllChangesCommand.Execute(null);
        CollectionAssert.AreEqual(new[] { 10, 20, 30 }, original.Select(line => line.SortNo).ToArray());
        await f.Vm.RefreshCommand.ExecuteAsync(null);
        Assert.IsFalse(f.Vm.MoveLineTo(original[0], 1));
    });

    /// <summary>真实表格仅允许手柄拖单行，连续跨行与滚动回收后仍移动实际行；筛选和外部来源不能绕过门禁。</summary>
    [TestMethod]
    [DataRow(3)]
    [DataRow(200)]
    public void GridDragHandlesRespectSearchAndVirtualization(int count) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        foreach (var file in new[] { "Variables.xaml", "Colors.xaml", "Typography.xaml", "Grids.xaml", "Borders.xaml", "TextBoxes.xaml", "Button.xaml", "DataGrid.xaml" })
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/{file}") });
        using var f = new Fixture(count);
        await f.Vm.InitializeAsync();
        var view = new ProductColorsView { DataContext = f.Vm };
        var window = new Window { Content = view, Width = 1200, Height = 800, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName("ColorsGrid");
            Assert.IsFalse(grid.CanUserSortColumns);
            Assert.IsTrue(grid.EnableRowVirtualization);
            Assert.IsTrue(grid.Columns.Any(column => Equals(column.Header, "排序号") && column.IsReadOnly));
            grid.UpdateLayout();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            var handle = Handle(row);
            Assert.IsTrue(handle.IsEnabled);
            var first = f.Vm.Lines.Items[0];
            var source = GongDragDrop.GetDragHandler(grid);
            var name = Descendants(row).OfType<TextBlock>().First(text => text.Text == first.ColorCodeUnique);
            Assert.IsFalse(source.CanStartDrag(DragInfo(grid, name)));
            var drag = DragInfo(grid, handle);
            Assert.IsTrue(source.CanStartDrag(drag));
            if (count == 3)
            {
                // 原生行验证未通过时不能离开编辑事务，也不能发出可移动载荷。
                grid.RowValidationRules.Add(new RejectReorderEditRule());
                grid.CurrentCell = new DataGridCellInfo(first, grid.Columns.OfType<DataGridTextColumn>().First(column => Equals(column.Header, "唯一颜色代码")));
                Assert.IsTrue(grid.BeginEdit());
                source.StartDrag(drag);
                Assert.AreEqual(DragDropEffects.None, drag.Effects);
                Assert.IsNull(drag.Data);
                grid.RowValidationRules.Clear();
                grid.CancelEdit(DataGridEditingUnit.Row);
            }
            f.Vm.Lines.Items[1].IsChecked = true;
            source.StartDrag(drag);
            Assert.AreSame(first, drag.Data);
            var drop = DropInfo(grid, drag, first, count);
            var target = GongDragDrop.GetDropHandler(grid);
            target.DragOver(DropInfo(grid, drag, first, -1));
            target.Drop(DropInfo(grid, drag, first, count + 1));
            Assert.IsFalse(f.Vm.HasChanges);
            target.DragOver(drop);
            Assert.AreEqual(DragDropEffects.Move, drop.Effects);
            target.Drop(drop);
            Assert.AreSame(first, f.Vm.Lines.Items.Last());
            Assert.AreSame(first, f.Vm.SelectedLine);
            Assert.IsTrue(f.Vm.Lines.Items[0].IsChecked);
            CollectionAssert.AreEqual(Enumerable.Range(1, count).ToArray(), f.Vm.Lines.Items.Select(line => line.SortNo).ToArray());
            foreach (var index in new[] { count / 2, 0, count - 2 })
            {
                var line = f.Vm.Lines.Items[index];
                grid.ScrollIntoView(line);
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
                grid.UpdateLayout();
                row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(line);
                drag = DragInfo(grid, Handle(row));
                Assert.IsTrue(source.CanStartDrag(drag));
                source.StartDrag(drag);
                Assert.AreSame(line, drag.Data);
                target.Drop(DropInfo(grid, drag, line, index == 0 ? count : 0));
                Assert.AreSame(line, index == 0 ? f.Vm.Lines.Items.Last() : f.Vm.Lines.Items[0]);
            }
            var current = f.Vm.Lines.Items[0];
            // 相邻插入槽为原地操作，外部表格及拖动中搜索变化必须在 Drop 再次阻断。
            drop = DropInfo(grid, drag, current, 1);
            target.DragOver(drop);
            Assert.AreEqual(DragDropEffects.None, drop.Effects);
            var before = f.Vm.Lines.Items.ToArray();
            target.Drop(DropInfo(new DataGrid(), drag, current, count));
            var external = DispatchProxy.Create<IDragInfo, Proxy>();
            ((Proxy)external).Call = (name, _) => name == "get_VisualSource" ? new DataGrid() : null;
            target.Drop(DropInfo(grid, external, current, count));
            f.Vm.Keyword = "蓝色";
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            grid.UpdateLayout();
            row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsFalse(Handle(row).IsEnabled);
            Assert.IsFalse(source.CanStartDrag(DragInfo(grid, Handle(row))));
            target.Drop(DropInfo(grid, drag, current, count));
            CollectionAssert.AreEqual(before, f.Vm.Lines.Items.ToArray());
            var lineToMove = (ProductColorLineModel)row.Item;
            f.Vm.Keyword = string.Empty;
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            grid.ScrollIntoView(lineToMove);
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            grid.UpdateLayout();
            row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromItem(lineToMove);
            Assert.IsTrue(Handle(row).IsEnabled);
            source = GongDragDrop.GetDragHandler(grid);
            target = GongDragDrop.GetDropHandler(grid);
            var restoredDrag = DragInfo(grid, Handle(row));
            source.StartDrag(restoredDrag);
            typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(f.Vm, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsFalse(Handle(row).IsEnabled);
            Assert.IsFalse(source.CanStartDrag(DragInfo(grid, Handle(row))));
            var readOnlyDrop = DropInfo(grid, restoredDrag, lineToMove, count);
            target.DragOver(readOnlyDrop);
            Assert.AreEqual(DragDropEffects.None, readOnlyDrop.Effects);
            target.Drop(readOnlyDrop);
            CollectionAssert.AreEqual(before, f.Vm.Lines.Items.ToArray());
            typeof(ViewModelBaseEx).GetProperty(nameof(ViewModelBaseEx.IsReadOnly))!.SetValue(f.Vm, false);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            target = GongDragDrop.GetDropHandler(grid);
            target.Drop(DropInfo(grid, restoredDrag, lineToMove, count));
            Assert.AreSame(lineToMove, f.Vm.Lines.Items.Last());
            if (count > 3) Assert.IsTrue(Descendants(grid).OfType<DataGridRow>().Count() < count);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>按真实手柄中心构造 Gong 拖拽上下文，覆盖当前视觉命中和数据对象解析。</summary>
    private static IDragInfo DragInfo(DataGrid grid, FrameworkElement handle) => new DragInfo(grid, handle, MouseButton.Left,
        target => handle.TranslatePoint(new Point(handle.ActualWidth / 2, handle.ActualHeight / 2), (UIElement)target));

    private static TextBlock Handle(DataGridRow row) => Descendants(row).OfType<TextBlock>().Single(text => text.Name == "ColorReorderHandle");

    /// <summary>模拟原生表格行校验失败，验证拖拽桥接尊重未提交编辑事务。</summary>
    private sealed class RejectReorderEditRule() : ValidationRule(ValidationStep.UpdatedValue, false)
    {
        /// <summary>以稳定错误阻断提交，不依赖真实服务或修改业务校验规则。</summary>
        public override ValidationResult Validate(object value, System.Globalization.CultureInfo cultureInfo) => new(false, "当前行未通过校验。");
    }

    /// <summary>提供库已计算的插入槽及来源；效果与插入线状态保留以验证真实 DropHandler。</summary>
    private static IDropInfo DropInfo(DataGrid grid, IDragInfo source, ProductColorLineModel line, int slot)
    {
        var values = new Dictionary<string, object?>
        {
            ["VisualTarget"] = grid, ["DragInfo"] = source, ["Data"] = line, ["InsertIndex"] = slot,
            ["TargetItem"] = slot >= 0 && slot < grid.Items.Count ? grid.Items[slot] : null,
            ["InsertPosition"] = RelativeInsertPosition.BeforeTargetItem,
        };
        var info = DispatchProxy.Create<IDropInfo, Proxy>();
        ((Proxy)info).Call = (name, args) =>
        {
            if (name.StartsWith("set_", StringComparison.Ordinal)) { values[name[4..]] = args[0]; return null; }
            return values.GetValueOrDefault(name[4..]);
        };
        return info;
    }

    /// <summary>遍历实际视觉树，检查呈现的手柄而不是框架内部字段。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    /// <summary>受控接口代理只实现本次实际调用，不连接网络或用户数据。</summary>
    public class Proxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Call(targetMethod!.Name, args!);
    }

    /// <summary>拥有完整服务器快照、可控保存结果和缓存刷新计数。</summary>
    private sealed class Fixture : IDisposable
    {
        public ProductColorsViewModel Vm { get; }
        public int Loads, Confirms, CacheRefreshes;
        public bool SaveSuccess = true, CacheSuccess = true;
        public ProductColorBatchSaveRequest? LastSave;
        public TaskCompletionSource<ServiceResult<List<ProductColorDto>>>? LoadGate;
        public List<string> Messages { get; } = [];
        private List<ProductColorDto> _rows = [Color("A", "RED", "Red", "红色", 10), Color("B", "BLUE", "Deep-Blue", "蓝色", 20), Color("C", "GREEN", "Green", "绿色", 30)];

        /// <summary>仅模拟读取、保存和当前颜色缓存刷新，其他调用立即报告测试失败。</summary>
        public Fixture(int count = 3)
        {
            if (count > 3) _rows.AddRange(Enumerable.Range(4, count - 3).Select(index => Color(index.ToString(), "CODE-" + index, "Color " + index, "颜色" + index, index * 10)));
            var api = Create<IProductColorApi>((name, args) => name switch
            {
                "GetAllAsync" => Load(),
                "BatchSaveAsync" => Save((ProductColorBatchSaveRequest)args[0]!),
                _ => throw new AssertFailedException(name),
            });
            var messages = Create<IMessageService>((_, args) => { Messages.Add((string)args[0]!); return null; });
            var dialog = Create<IDialogService>((name, _) => name == "ConfirmAsync" ? Confirm() : throw new AssertFailedException(name));
            var cache = Create<IReferenceDataCacheService>((name, _) => name == "RefreshProductColorsAsync" ? RefreshCache() : throw new AssertFailedException(name));
            Vm = new(api, new SystemParameterClientFixture.DirectRunner(), messages, dialog, cache);
        }

        private Task<ServiceResult<List<ProductColorDto>>> Load()
        { Loads++; return LoadGate?.Task ?? Task.FromResult(ServiceResult<List<ProductColorDto>>.Ok(_rows.Adapt<List<ProductColorDto>>())); }
        private Task<bool> Confirm() { Confirms++; return Task.FromResult(true); }
        private Task<CacheRefreshResult> RefreshCache()
        { CacheRefreshes++; return Task.FromResult(CacheSuccess ? CacheRefreshResult.Ok("刷新成功") : CacheRefreshResult.Fail("刷新失败")); }

        /// <summary>成功时发布独立服务端快照，失败时保留原结果，不替页面计算预期顺序。</summary>
        private Task<ServiceResult<ProductColorBatchSaveResponse>> Save(ProductColorBatchSaveRequest request)
        {
            LastSave = request;
            if (!SaveSuccess) return Task.FromResult(ServiceResult<ProductColorBatchSaveResponse>.Fail("版本冲突"));
            _rows = request.UpdateItems.Select(item => item.Data.Adapt<ProductColorDto>()).OrderBy(color => color.SortNo).ToList();
            return Task.FromResult(ServiceResult<ProductColorBatchSaveResponse>.Ok(new()));
        }

        private static ProductColorDto Color(string code, string alias, string en, string zh, int sortNo) => new()
        { Uid = Guid.NewGuid(), Version = 4, ColorCodeUnique = "UNIQUE-" + code, ColorCode = alias, ColorNameEn = en, ColorNameZh = zh, SortNo = sortNo };
        private static T Create<T>(Func<string, object?[], object?> call) where T : class
        {
            var proxy = DispatchProxy.Create<T, Proxy>();
            ((Proxy)(object)proxy).Call = call;
            return proxy;
        }
        /// <summary>释放页面生命周期及列表订阅。</summary>
        public void Dispose() => Vm.Dispose();
    }
}

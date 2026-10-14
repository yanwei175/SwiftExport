using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>验证中性产品包装的真实远程搜索交互，以及物料列的确认和清空绑定。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductNeutralLookupTextBoxTests
{
    /// <summary>初始化和候选替换不搜索；选中身份、候选颜色和图片均读取同一来源。</summary>
    [TestMethod]
    public void Wrapper_InitializesWithoutSearchingAndDisplaysCandidate() => Run(async () =>
    {
        var first = Item("NEUTRAL-A", "镀铬");
        var second = Item("NEUTRAL-B", "哑黑");
        var search = new RecordingCommand();
        var lookup = new ProductNeutralLookupTextBox
        {
            ItemsSource = new[] { first, second }, SelectedProductNeutral = first,
            SearchCommand = search, FontSize = 15
        };
        var sink = new ContentControl();
        BindingOperations.SetBinding(lookup, ProductNeutralLookupTextBox.SelectedProductNeutralIdProperty,
            new Binding(nameof(ContentControl.Tag)) { Source = sink, Mode = BindingMode.TwoWay });
        var editor = (LookupTextBox)lookup.FindName("LookupEditor");
        using var host = new Host(lookup);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        lookup.ItemsSource = new[] { second, first };
        lookup.SelectedProductNeutral = second;
        lookup.SelectedProductNeutral = first;
        await Task.Delay(350);
        Assert.AreEqual(first.Uid, lookup.SelectedProductNeutralId);
        Assert.AreEqual(first.Uid, sink.Tag);
        Assert.AreEqual("NEUTRAL-A", ((TextBlock)editor.FindName("DisplayTextBlock")).Text);
        Assert.AreEqual(15d, editor.FontSize);
        Assert.AreEqual(LookupTextBoxSearchMode.Remote, editor.SearchMode);
        Assert.AreEqual(0, search.Count);
        Assert.IsFalse(editor.IsDropDownOpen);

        var candidate = (Grid)editor.ItemTemplate!.LoadContent();
        candidate.DataContext = first;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual("NEUTRAL-A", candidate.Children.OfType<TextBlock>().First().Text);
        Assert.AreEqual("镀铬", candidate.Children.OfType<TextBlock>().Last().Text);
        var image = candidate.Children.OfType<CachedImage>().Single();
        Assert.AreEqual(first.Source.Data.ImagePath, image.SourcePath);
        Assert.AreEqual(48d, image.Width);
        Assert.AreEqual(96, image.DecodePixelWidth);
        Assert.IsNull(image.ToolTip);
    });

    /// <summary>仅最后输入发起远程查询，Esc 和失焦保留既有选择且失焦取消待执行搜索。</summary>
    [TestMethod]
    public void Wrapper_DebouncesAndPreservesSelectionOnCancel() => Run(async () =>
    {
        var first = Item("NEUTRAL-A", "镀铬");
        var search = new RecordingCommand();
        var clear = new RecordingCommand();
        var lookup = new ProductNeutralLookupTextBox
        {
            ItemsSource = new[] { first }, SelectedProductNeutral = first,
            SearchCommand = search, SelectionClearedCommand = clear,
            SearchDelay = TimeSpan.FromMilliseconds(40)
        };
        var editor = (LookupTextBox)lookup.FindName("LookupEditor");
        using var host = new Host(lookup);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        EnterEditing(editor);
        var input = (TextBox)editor.FindName("InputTextBox");
        input.Text = "n";
        input.Text = "ne";
        input.Text = "neutral";
        await Task.Delay(120);
        Assert.AreEqual(1, search.Count);
        Assert.AreEqual("neutral", search.Parameter);
        Key(input, System.Windows.Input.Key.Escape);
        Assert.AreSame(first, lookup.SelectedProductNeutral);
        Assert.AreEqual(first.Uid, lookup.SelectedProductNeutralId);
        Assert.IsFalse(editor.IsDropDownOpen);

        EnterEditing(editor);
        input.Text = "pending";
        host.Next.Focus();
        Keyboard.Focus(host.Next);
        await Task.Delay(120);
        Assert.AreEqual(1, search.Count);
        Assert.AreEqual(0, clear.Count);
        Assert.AreSame(first, lookup.SelectedProductNeutral);
        Assert.AreEqual(first.Uid, lookup.SelectedProductNeutralId);
        Assert.IsFalse(editor.IsDropDownOpen);
    });

    /// <summary>从实际物料页取得编辑模板并放入真实 DataGrid；仅确认和明确清空改写目标行。</summary>
    [TestMethod]
    public void ProductsColumn_UsesWrapperAndCommitsOnlyConfirmedIdentity() => Run(async () =>
    {
        var view = new ProductsView();
        using var host = new Host(view, 1300, 850);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var sourceColumn = Descendants(view).OfType<DataGrid>()
            .SelectMany(grid => grid.Columns.OfType<DataGridTemplateColumn>())
            .Single(column => Equals(column.Header, "产品型号"));
        var first = Item("NEUTRAL-A", "镀铬");
        var second = Item("NEUTRAL-B", "哑黑");
        var row = new ProductLineModel
        {
            ProductNeutralId = first.Uid, LinkSku = first.ProductCode!,
            ProductMasterId = Guid.NewGuid(), ColorId = Guid.NewGuid()
        };
        var other = new ProductLineModel { ProductNeutralId = first.Uid, LinkSku = "其他行" };
        var masterId = row.ProductMasterId;
        var colorId = row.ColorId;
        var context = new SelectionSink([first, second]);
        var column = new DataGridTemplateColumn
        {
            Header = sourceColumn.Header, Width = 360,
            CellTemplate = sourceColumn.CellTemplate, CellEditingTemplate = sourceColumn.CellEditingTemplate
        };
        var grid = new DataGrid
        {
            AutoGenerateColumns = false, CanUserAddRows = false, Height = 280,
            ItemsSource = new[] { row, other }, DataContext = context
        };
        grid.Columns.Add(column);
        host.SetContent(grid);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        grid.SelectedItem = row;
        grid.ScrollIntoView(row, column);
        grid.UpdateLayout();
        grid.CurrentCell = new DataGridCellInfo(row, column);
        Assert.IsTrue(grid.BeginEdit());
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        var lookup = Descendants(column.GetCellContent(row)).OfType<ProductNeutralLookupTextBox>().Single();
        var editor = (LookupTextBox)lookup.FindName("LookupEditor");
        Assert.AreEqual("NEUTRAL-A", lookup.Text);
        Assert.AreEqual(LookupTextBoxSearchMode.Remote, editor.SearchMode);
        Assert.AreEqual(0, context.SearchProductNeutralsCommand.Count);

        EnterEditing(editor);
        var input = (TextBox)editor.FindName("InputTextBox");
        input.Text = "尚未确认";
        Assert.AreEqual(first.Uid, row.ProductNeutralId);
        Assert.AreEqual("NEUTRAL-A", row.LinkSku);
        Key(input, System.Windows.Input.Key.Down);
        ((ListBox)editor.FindName("ResultList")).SelectedItem = second;
        Key(input, System.Windows.Input.Key.Enter);
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.AreSame(second, context.EditProductNeutral);
        Assert.AreSame(row, context.CommitProductNeutralCommand.Parameter);
        Assert.AreEqual(second.Uid, row.ProductNeutralId);
        Assert.AreEqual("NEUTRAL-B", row.LinkSku);
        ((Button)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        Assert.AreSame(row, context.ClearProductNeutralCommand.Parameter);
        Assert.IsNull(row.ProductNeutralId);
        Assert.AreEqual(string.Empty, row.LinkSku);
        Assert.AreEqual(masterId, row.ProductMasterId);
        Assert.AreEqual(colorId, row.ColorId);
        Assert.AreEqual(first.Uid, other.ProductNeutralId);
        Assert.AreEqual("其他行", other.LinkSku);
    });

    /// <summary>复用持续存活的 STA Application 和实际资源，覆盖编译后的页面模板。</summary>
    private static void Run(Func<Task> action) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        await action();
    });

    private static ProductNeutralLookupItemModel Item(string code, string color) => new(new()
    { Uid = Guid.NewGuid(), ColorName = color, Data = new() { ProductCode = code, ImagePath = "fixture.png" } });

    /// <summary>通过用户点击进入输入态，不依赖控件内部状态字段。</summary>
    private static void EnterEditing(LookupTextBox editor)
    {
        var border = (Border)editor.FindName("EditorBorder");
        border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = border });
    }

    private static void Key(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    /// <summary>枚举实际视觉树以取得原生列的编辑器，不解析或匹配 XAML 源文本。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>仅记录模板的绑定输入；测试提交模拟接纳选中值，不访问 API 或保存业务。</summary>
    private sealed class SelectionSink(ProductNeutralLookupItemModel[] candidates)
    {
        public ProductNeutralLookupItemModel[] ProductNeutralCandidates { get; } = candidates;
        public ProductNeutralLookupItemModel? EditProductNeutral { get; set; }
        public bool IsProductNeutralLookupBusy => false;
        public RecordingCommand SearchProductNeutralsCommand { get; } = new();
        public RecordingCommand CommitProductNeutralCommand => _commit ??= new(parameter =>
        {
            var row = (ProductLineModel)parameter!;
            row.ProductNeutralId = EditProductNeutral!.Uid;
            row.LinkSku = EditProductNeutral.ProductCode!;
        });
        public RecordingCommand ClearProductNeutralCommand { get; } = new(parameter =>
        {
            var row = (ProductLineModel)parameter!;
            row.ProductNeutralId = null;
            row.LinkSku = string.Empty;
        });
        private RecordingCommand? _commit;
    }

    /// <summary>提供真实焦点目标，并确保失败后窗口仍会关闭。</summary>
    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        private readonly StackPanel _panel = new();
        public Button Next { get; } = new() { Content = "下一项" };
        /// <summary>以真实窗口承载当前编辑控件及失焦目标。</summary>
        public Host(UIElement control, double width = 480, double height = 180)
        {
            _panel.Children.Add(control);
            _panel.Children.Add(Next);
            _window = new Window { Width = width, Height = height, Content = _panel, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            _window.Show();
            _window.Activate();
        }
        /// <summary>保留窗口和焦点目标，将页面切换为使用真实列模板的表格。</summary>
        public void SetContent(UIElement control) { _panel.Children.RemoveAt(0); _panel.Children.Insert(0, control); }
        /// <summary>释放真实窗口及其视觉树。</summary>
        public void Dispose() => _window.Close();
    }

    /// <summary>记录公开命令及业务行参数，以检验初始化零请求和显式选择事件。</summary>
    private sealed class RecordingCommand(Action<object?>? execute = null) : ICommand
    {
        public int Count { get; private set; }
        public object? Parameter { get; private set; }
        /// <summary>允许控件向测试接收端发出操作。</summary>
        public bool CanExecute(object? parameter) => true;
        /// <summary>记录交互参数，并执行测试选值接纳。</summary>
        public void Execute(object? parameter) { Count++; Parameter = parameter; execute?.Invoke(parameter); }
        /// <summary>测试命令始终可用，不发布准入变化。</summary>
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}

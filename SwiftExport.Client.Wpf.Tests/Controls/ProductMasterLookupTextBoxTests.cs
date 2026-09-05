using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.Components.Models;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>验证主型号候选完整字段及业务包装的真实绑定、键盘和远程搜索行为。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterLookupTextBoxTests
{
    /// <summary>所有顶层字段直接读取原 DTO，查询结果不被复制为第二份编辑状态。</summary>
    [TestMethod]
    public void Candidate_PreservesAllFieldsAndSource()
    {
        var uid = Guid.NewGuid();
        var category = Guid.NewGuid();
        var supplier = Guid.NewGuid();
        var created = new DateTime(2026, 1, 1);
        var updated = created.AddDays(1);
        var dto = new ProductMasterListItemDto
        {
            Uid = uid, Version = 7, IsDeleted = true, CreatedDate = created, CreatedBy = "创建人", UpdatedDate = updated, UpdatedBy = "更新人",
            CategoryName = "分类", DefaultSupplierName = "供应商", MainImagePath = "image.png", DefaultSalesPriceUsd = 12m, DefaultPurchasePriceCny = 50m,
            Data = new() { MasterSku = "MASTER", DefaultDescriptionEn = "English", DefaultDescriptionZh = "中文",
                DefaultFeaturesEn = "Features", DefaultFeaturesZh = "配置", DefaultPcsPerPackage = 6,
                DefaultNetWeight = 2m, DefaultGrossWeight = 3m, DefaultCbm = 0.1m, ProductType = "类型", ProductTag = "标签",
                CategoryId = category, DefaultSupplierId = supplier }
        };
        var item = new ProductMasterLookupItemModel(dto);
        Assert.AreSame(dto, item.Source);
        CollectionAssert.AreEqual(new object?[] { uid, 7, true, created, "创建人", updated, "更新人", "MASTER", "English", "中文", "Features", "配置",
            6, 2m, 3m, 0.1m, "类型", "标签", category, "分类", supplier, "供应商", "image.png", 12m, 50m },
            new object?[] { item.Uid, item.Version, item.IsDeleted, item.CreatedDate, item.CreatedBy, item.UpdatedDate, item.UpdatedBy,
                item.MasterSku, item.DefaultDescriptionEn, item.DefaultDescriptionZh, item.DefaultFeaturesEn, item.DefaultFeaturesZh,
                item.DefaultPcsPerPackage, item.DefaultNetWeight, item.DefaultGrossWeight, item.DefaultCbm, item.ProductType, item.ProductTag,
                item.CategoryId, item.CategoryName, item.DefaultSupplierId, item.DefaultSupplierName, item.MainImagePath,
                item.DefaultSalesPriceUsd, item.DefaultPurchasePriceCny });
        dto.Data.ProductTag = "来源新值";
        Assert.AreEqual("来源新值", item.ProductTag);
    }

    /// <summary>可选空值不被伪装为零价格；缺少嵌套数据安全显示，空来源明确拒绝。</summary>
    [TestMethod]
    public void Candidate_HandlesEmptyValues()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() => new ProductMasterLookupItemModel(null!));
        var item = new ProductMasterLookupItemModel(new() { Data = null! });
        Assert.IsNull(item.MasterSku);
        Assert.IsNull(item.DefaultDescriptionZh);
        Assert.IsNull(item.MainImagePath);
        Assert.IsNull(item.DefaultNetWeight);
        Assert.IsNull(item.DefaultSalesPriceUsd);
        Assert.IsNull(item.DefaultPurchasePriceCny);
        Assert.AreEqual(Guid.Empty, item.CategoryId);
        Assert.AreEqual(Guid.Empty, item.DefaultSupplierId);
        Assert.AreEqual(1, item.DefaultPcsPerPackage);
    }

    /// <summary>使用真实应用资源验证选中回填、模板、提交及清空参数，初始化绝不搜索。</summary>
    [TestMethod]
    public void Wrapper_BindsSelectionAndCommands() => Run(async () =>
    {
        var first = Item("A", "中文描述");
        var second = Item("B", "第二项");
        var commit = new RecordingCommand();
        var clear = new RecordingCommand();
        var search = new RecordingCommand();
        var parameter = new object();
        var lookup = new ProductMasterLookupTextBox
        {
            ItemsSource = new[] { first, second }, SelectedProductMaster = first, SearchCommand = search,
            SelectionCommittedCommand = commit, SelectionCommittedCommandParameter = parameter,
            SelectionClearedCommand = clear, SelectionClearedCommandParameter = parameter, FontSize = 15
        };
        // 通过真实双向 Binding 检查外部业务选中值是否获得回写。
        var selectedSink = new ContentControl();
        BindingOperations.SetBinding(lookup, ProductMasterLookupTextBox.SelectedProductMasterIdProperty,
            new Binding(nameof(ContentControl.Tag)) { Source = selectedSink, Mode = BindingMode.TwoWay });
        var editor = (LookupTextBox)lookup.FindName("LookupEditor");
        using var host = new Host(lookup);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        lookup.SelectedProductMaster = second;
        lookup.SelectedProductMaster = first;
        await Task.Delay(350);
        Assert.AreEqual(first.Uid, lookup.SelectedProductMasterId);
        Assert.AreEqual(first.Uid, selectedSink.Tag);
        Assert.AreEqual("A", ((TextBlock)editor.FindName("DisplayTextBlock")).Text);
        Assert.AreEqual(15d, editor.FontSize);
        Assert.AreEqual(TimeSpan.FromMilliseconds(300), editor.SearchDelay);
        Assert.AreEqual(1, editor.MinimumSearchLength);
        Assert.IsFalse(editor.ClearSelectionOnEdit);
        Assert.IsTrue(editor.RequireItemMatch);
        Assert.AreEqual(0, search.Count);
        Assert.IsFalse(editor.IsDropDownOpen);

        Assert.IsNotNull(editor.ItemTemplate);
        var template = (Grid)editor.ItemTemplate.LoadContent();
        template.DataContext = first;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual("A", template.Children.OfType<TextBlock>().First().Text);
        Assert.AreEqual("中文描述", template.Children.OfType<TextBlock>().Last().Text);
        var image = template.Children.OfType<CachedImage>().Single();
        Assert.AreEqual(48d, image.Width);
        Assert.AreEqual(96, image.DecodePixelWidth);
        Assert.IsNull(image.ToolTip);

        EnterEditing(editor);
        var input = (TextBox)editor.FindName("InputTextBox");
        Key(input, System.Windows.Input.Key.Down);
        var list = (ListBox)editor.FindName("ResultList");
        list.SelectedItem = second;
        Key(input, System.Windows.Input.Key.Enter);
        await Dispatcher.Yield(DispatcherPriority.Background);
        Assert.AreSame(second, lookup.SelectedProductMaster);
        Assert.AreEqual(second.Uid, selectedSink.Tag);
        Assert.AreEqual(1, commit.Count);
        Assert.AreSame(parameter, commit.Parameter);
        ((Button)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.IsNull(lookup.SelectedProductMaster);
        Assert.IsNull(lookup.SelectedProductMasterId);
        Assert.IsNull(selectedSink.Tag);
        Assert.AreEqual(1, clear.Count);
        Assert.AreSame(parameter, clear.Parameter);
    });

    /// <summary>快速输入只搜索最后关键词，Esc 与失焦均不解除原选中，失焦取消待执行防抖。</summary>
    [TestMethod]
    public void Wrapper_DebouncesAndPreservesSelection() => Run(async () =>
    {
        var first = Item("A", "中文");
        var search = new RecordingCommand();
        var clear = new RecordingCommand();
        var lookup = new ProductMasterLookupTextBox
        {
            ItemsSource = new[] { first }, SelectedProductMaster = first,
            SearchCommand = search, SelectionClearedCommand = clear, SearchDelay = TimeSpan.FromMilliseconds(40)
        };
        var editor = (LookupTextBox)lookup.FindName("LookupEditor");
        using var host = new Host(lookup);
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        EnterEditing(editor);
        var input = (TextBox)editor.FindName("InputTextBox");
        input.Text = "b";
        input.Text = "bc";
        input.Text = "bcd";
        await Task.Delay(120);
        Assert.AreEqual(1, search.Count);
        Assert.AreEqual("bcd", search.Parameter);
        Key(input, System.Windows.Input.Key.Escape);
        Assert.AreSame(first, lookup.SelectedProductMaster);
        Assert.AreEqual(first.Uid, lookup.SelectedProductMasterId);
        Assert.IsFalse(editor.IsDropDownOpen);
        EnterEditing(editor);
        input.Text = "pending";
        host.Next.Focus();
        Keyboard.Focus(host.Next);
        await Task.Delay(120);
        Assert.AreEqual(1, search.Count);
        Assert.AreSame(first, lookup.SelectedProductMaster);
        Assert.AreEqual(first.Uid, lookup.SelectedProductMasterId);
        Assert.AreEqual(0, clear.Count);
        Assert.IsFalse(editor.IsDropDownOpen);
    });

    /// <summary>复用现有持续存活的 STA Application，加载实际主题及 ListBox 字典，不创建假样式。</summary>
    private static void Run(Func<Task> action) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        { Source = new Uri("pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/ListBox.xaml") });
        await action();
    });

    private static ProductMasterLookupItemModel Item(string sku, string description) => new(new()
    { Uid = Guid.NewGuid(), Data = new() { MasterSku = sku, DefaultDescriptionZh = description } });

    private static void EnterEditing(LookupTextBox editor)
    {
        var border = (Border)editor.FindName("EditorBorder");
        border.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = UIElement.MouseLeftButtonDownEvent, Source = border });
    }

    private static void Key(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });

    /// <summary>提供真实焦点目标，并在断言失败时也关闭测试窗口。</summary>
    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        public Button Next { get; } = new() { Content = "下一项" };
        public Host(UIElement control)
        {
            var panel = new StackPanel();
            panel.Children.Add(control);
            panel.Children.Add(Next);
            _window = new Window { Width = 480, Height = 180, Content = panel, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            _window.Show();
            _window.Activate();
        }
        public void Dispose() => _window.Close();
    }

    /// <summary>记录公开命令输入，不调用网络或业务保存。</summary>
    private sealed class RecordingCommand : ICommand
    {
        public int Count { get; private set; }
        public object? Parameter { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { Count++; Parameter = parameter; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}

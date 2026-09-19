using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Customers.Components;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Suppliers.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>验证三个缓存业务 ComboBox 复用 LookupTextBox 的本地选择、按钮和字体行为。</summary>
[TestClass]
[DoNotParallelize]
public sealed class CachedAutoSuggestComboBoxTests
{
    /// <summary>验证供应商封装的开关可动态撑满宿主，并在关闭后恢复内容高度。</summary>
    [TestMethod]
    public void SupplierStretchToHost_ShouldFillAndRestoreContentHeight()
    {
        StaTest.RunAsync(() =>
        {
            var supplier = new SupplierAutoSuggestComboBox();
            var host = new Grid { Width = 240, Height = 120 };
            host.Children.Add(supplier);
            var border = (Border)Editor(supplier).FindName("EditorBorder");

            void Layout()
            {
                host.Measure(new Size(240, 120));
                host.Arrange(new Rect(0, 0, 240, 120));
                host.UpdateLayout();
            }

            Layout();
            var contentHeight = border.ActualHeight;
            Assert.IsTrue(contentHeight > 0 && contentHeight < 120);
            supplier.StretchToHost = true;
            Layout();
            Assert.AreEqual(120d, border.ActualHeight, 0.1);
            supplier.StretchToHost = false;
            Layout();
            Assert.AreEqual(contentHeight, border.ActualHeight, 0.1);
            return Task.CompletedTask;
        });
    }

    /// <summary>验证新封装固定本地单字段筛选，旧远程封装保持默认无箭头。</summary>
    [TestMethod]
    public void SpecializedControls_ShouldFixLocalConfigurationWithoutChangingLegacyDefaults()
    {
        StaTest.RunAsync(() =>
        {
            var color = new ProductColorAutoSuggestComboBox { FontSize = 17 };
            var customer = new CustomerAutoSuggestComboBox { FontSize = 18 };
            var supplier = new SupplierAutoSuggestComboBox { FontSize = 19 };

            AssertConfiguration(Editor(color), nameof(ProductColorDto.ColorNameZh), 17);
            AssertConfiguration(Editor(customer), nameof(CustomerDto.CustomerCode), 18);
            AssertConfiguration(Editor(supplier), nameof(SupplierDto.SupplierCode), 19);

            var legacyLookup = new LookupTextBox();
            Assert.IsFalse(legacyLookup.ShowDropDownButton);
            Assert.IsFalse(legacyLookup.MatchSuggestionFontSize);

            var productLookup = new ProductLookupTextBox();
            var productEditor = Editor(productLookup);
            Assert.AreEqual(LookupTextBoxSearchMode.Remote, productEditor.SearchMode);
            Assert.IsFalse(productEditor.ShowDropDownButton);
            return Task.CompletedTask;
        });
    }

    /// <summary>验证箭头在已有选择时展开全部、保留选择和焦点，并可靠收起 StaysOpen 弹层。</summary>
    [TestMethod]
    public void DropDownButton_WithCommittedSelection_ShouldToggleWithoutEditingSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var first = Customer("C001", "甲客户");
            var second = Customer("C002", "乙客户");
            var control = new CustomerAutoSuggestComboBox
            {
                ItemsSource = new ObservableCollection<CustomerDto> { first, second },
                SelectedCustomer = first,
            };
            var next = new Button { Content = "下一项" };
            using var host = new ControlHost(control, next);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var editor = Editor(control);
            var button = (Button?)editor.FindName("BtnDropDown");
            var results = (ListBox?)editor.FindName("ResultList");
            Assert.IsNotNull(button);
            Assert.IsNotNull(results);
            next.Focus();
            Keyboard.Focus(next);

            RaiseMouseDown(button);
            Assert.AreSame(next, Keyboard.FocusedElement, "箭头按下不得抢走编辑焦点。");
            RaiseMouseUp(button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            await Dispatcher.Yield(DispatcherPriority.Background);

            Assert.IsTrue(editor.IsDropDownOpen);
            Assert.AreEqual(2, results.Items.Count);
            Assert.AreSame(first, control.SelectedCustomer);
            Assert.AreSame(next, Keyboard.FocusedElement);

            RaiseMouseDown(button);
            editor.IsDropDownOpen = false; // 模拟 StaysOpen=false 在 Click 前关闭 Popup。
            RaiseMouseUp(button);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, button));
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsFalse(editor.IsDropDownOpen, "再次点击箭头不得在 Popup 自动关闭后重新打开。");
            Assert.AreSame(first, control.SelectedCustomer);
        });
    }

    /// <summary>验证真实候选容器的按下和释放路径提交完整 DTO，并由清空按钮解除选择一次。</summary>
    [TestMethod]
    public void MouseAndClear_ShouldCommitFullDtoAndPreserveBindingSemantics()
    {
        StaTest.RunAsync(async () =>
        {
            var first = Supplier("S001", "甲供应商");
            var second = Supplier("S002", "乙供应商");
            var committed = new RecordingCommand();
            var cleared = new RecordingCommand();
            var context = new object();
            var control = new SupplierAutoSuggestComboBox
            {
                ItemsSource = new ObservableCollection<SupplierDto> { first, second },
                SelectionCommittedCommand = committed,
                SelectionCommittedCommandParameter = context,
                SelectionClearedCommand = cleared,
            };
            using var host = new ControlHost(control);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var editor = Editor(control);
            var input = (TextBox?)editor.FindName("InputTextBox");
            var results = (ListBox?)editor.FindName("ResultList");
            Assert.IsNotNull(input);
            Assert.IsNotNull(results);
            input.Focus();
            Keyboard.Focus(input);
            input.Text = "S002";
            await Dispatcher.Yield(DispatcherPriority.Background);
            results.UpdateLayout();
            var item = (ListBoxItem?)results.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(item);

            RaiseMouseDown(item);
            Assert.IsTrue(input.IsKeyboardFocusWithin, "不可聚焦的候选容器按下时应保留输入焦点。");
            RaisePreviewMouseUp(item);
            await Dispatcher.Yield(DispatcherPriority.Background);

            Assert.AreSame(second, control.SelectedSupplier);
            Assert.AreEqual(second.Uid, control.SelectedSupplier?.Uid);
            Assert.AreEqual(1, committed.ExecutionCount);
            Assert.AreSame(context, committed.LastParameter);

            var clear = (Button?)editor.FindName("BtnClear");
            Assert.IsNotNull(clear);
            clear.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, clear));
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsNull(control.SelectedSupplier);
            Assert.AreEqual(1, cleared.ExecutionCount);
        });
    }

    /// <summary>验证键盘 Enter 与 Tab 沿用 LookupTextBox 的既有确认路径。</summary>
    [TestMethod]
    public void KeyboardConfirmation_ShouldSupportEnterAndTab()
    {
        StaTest.RunAsync(async () =>
        {
            var color = new ProductColorDto { Uid = Guid.NewGuid(), ColorNameZh = "深蓝" };
            var enterCommand = new RecordingCommand();
            var enterControl = new ProductColorAutoSuggestComboBox
            {
                ItemsSource = new ObservableCollection<ProductColorDto> { color },
                SelectionCommittedCommand = enterCommand,
            };
            using (var host = new ControlHost(enterControl))
            {
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = Editor(enterControl);
                var input = (TextBox)editor.FindName("InputTextBox")!;
                input.Focus(); Keyboard.Focus(input);
                RaisePreviewKey(input, Key.Down);
                RaisePreviewKey(input, Key.Enter);
                Assert.AreSame(color, enterControl.SelectedColor);
                Assert.AreEqual(1, enterCommand.ExecutionCount);
            }

            var customer = Customer("C001", "甲客户");
            var tabCommand = new RecordingCommand();
            var tabControl = new CustomerAutoSuggestComboBox
            {
                ItemsSource = new ObservableCollection<CustomerDto> { customer },
                SelectionCommittedCommand = tabCommand,
            };
            using var tabHost = new ControlHost(tabControl);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var tabEditor = Editor(tabControl);
            var tabInput = (TextBox)tabEditor.FindName("InputTextBox")!;
            tabInput.Focus(); Keyboard.Focus(tabInput);
            RaisePreviewKey(tabInput, Key.Down);
            RaisePreviewKey(tabInput, Key.Tab);
            Assert.AreSame(customer, tabControl.SelectedCustomer);
            Assert.AreEqual(1, tabCommand.ExecutionCount);
        });
    }

    private static void AssertConfiguration(LookupTextBox editor, string memberPath, double fontSize)
    {
        Assert.AreEqual(LookupTextBoxSearchMode.Local, editor.SearchMode);
        Assert.AreEqual(memberPath, editor.DisplayMemberPath);
        Assert.AreEqual(memberPath, editor.FilterMemberPaths);
        Assert.IsTrue(editor.RequireItemMatch);
        Assert.IsFalse(editor.ClearSelectionOnEdit);
        Assert.IsTrue(editor.ShowDropDownButton);
        Assert.IsTrue(editor.MatchSuggestionFontSize);
        Assert.AreEqual(fontSize, editor.FontSize);
        var results = (ListBox?)editor.FindName("ResultList");
        Assert.IsNotNull(results);
        Assert.AreEqual(fontSize, results.FontSize);
    }

    private static LookupTextBox Editor(FrameworkElement control) =>
        (LookupTextBox)(control.FindName("LookupEditor") ?? throw new AssertFailedException("业务封装缺少 LookupEditor。"));

    private static CustomerDto Customer(string code, string name) =>
        new() { Uid = Guid.NewGuid(), CustomerCode = code, CustomerName = name };

    private static SupplierDto Supplier(string code, string name) =>
        new() { Uid = Guid.NewGuid(), SupplierCode = code, SupplierName = name };

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
                     ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private static void RaiseMouseDown(UIElement target) => target.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
            Source = target,
        });

    private static void RaiseMouseUp(UIElement target) => target.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent,
            Source = target,
        });

    private static void RaisePreviewMouseUp(UIElement target) => target.RaiseEvent(
        new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseUpEvent,
            Source = target,
        });

    private sealed class ControlHost : IDisposable
    {
        private readonly Window _window;

        public ControlHost(UIElement control, UIElement? additional = null)
        {
            var panel = new StackPanel();
            panel.Children.Add(control);
            if (additional is not null) panel.Children.Add(additional);
            _window = new Window { Width = 520, Height = 240, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = panel };
            _window.Show();
            _window.Activate();
        }

        public void Dispose() => _window.Close();
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecutionCount { get; private set; }
        public object? LastParameter { get; private set; }
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) { ExecutionCount++; LastParameter = parameter; }
        public event EventHandler? CanExecuteChanged { add { } remove { } }
    }
}

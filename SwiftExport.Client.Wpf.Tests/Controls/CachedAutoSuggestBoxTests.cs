using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Data;
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
using AutoSuggestionBoxTextChangeReason = global::Wpf.Ui.Controls.AutoSuggestionBoxTextChangeReason;
using WpfTextBox = global::Wpf.Ui.Controls.TextBox;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>
/// 验证三个缓存 AutoSuggestBox 的本地筛选、提交边界、字体传递和多实例隔离。
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CachedAutoSuggestBoxTests
{
    /// <summary>
    /// 验证客户候选只按编码筛选，浏览不提交，Enter 确认一次且失焦恢复已提交显示。
    /// </summary>
    [TestMethod]
    public void CustomerAutoSuggestBox_ShouldFilterLocallyAndCommitOnlyOnEnter()
    {
        Run(async () =>
        {
            var first = Customer("C001", "甲客户");
            var second = Customer("C002", "乙客户");
            var committed = new RecordingCommand();
            var cleared = new RecordingCommand();
            var control = new CustomerAutoSuggestBox
            {
                ItemsSource = new ObservableCollection<CustomerDto> { first, second },
                SelectionCommittedCommand = committed,
                SelectionClearedCommand = cleared,
            };

            using var host = new ControlHost(control);
            await PumpAsync();
            var editor = GetEditor(control);
            var input = GetInput(editor);
            var list = GetSuggestionList(editor);

            input.Text = "C002";
            await PumpAsync();
            Assert.AreEqual("C002", control.SearchText);
            Assert.AreEqual(1, editor.Items.Count);
            Assert.AreSame(second, editor.Items[0]);

            list.SelectedItem = editor.Items[0];
            await PumpAsync();
            Assert.IsNull(control.SelectedCustomer, "方向键造成的候选高亮不得提前提交。");
            Assert.AreEqual(0, committed.Count);

            list.UpdateLayout();
            var highlightedItem = (global::Wpf.Ui.Controls.ListViewItem?)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(highlightedItem);
            RaisePreviewKey(highlightedItem, Key.Enter, editor);
            await PumpAsync();
            Assert.AreSame(second, control.SelectedCustomer);
            Assert.AreEqual(1, committed.Count);
            Assert.AreSame(second, committed.Parameter);
            Assert.AreEqual(string.Empty, control.SearchText);
            Assert.AreEqual("C002", input.Text);

            control.FocusEditor();
            await PumpAsync();
            input.Text = "C00";
            host.Next.Focus();
            Keyboard.Focus(host.Next);
            await PumpAsync();
            Assert.AreSame(second, control.SelectedCustomer);
            Assert.AreEqual("C002", input.Text);
            Assert.AreEqual(string.Empty, control.SearchText);
            Assert.AreEqual(1, committed.Count);

            input.Focus();
            RaiseUserTextChange(editor, "X");
            RaiseUserTextChange(editor, string.Empty);
            await PumpAsync();
            Assert.IsNull(control.SelectedCustomer);
            Assert.AreEqual(1, cleared.Count);
            Assert.IsFalse(control.IsSuggestionListOpen);
        });
    }

    /// <summary>
    /// 验证鼠标确认只执行一次，并在更新强类型属性之后执行外部命令。
    /// </summary>
    [TestMethod]
    public void SupplierAutoSuggestBox_MouseConfirmation_ShouldCommitOnce()
    {
        Run(async () =>
        {
            var supplier = Supplier("S001", "第一供应商");
            var context = new object();
            SupplierAutoSuggestBox? control = null;
            var committed = new CallbackCommand(() => Assert.AreSame(supplier, control!.SelectedSupplier));
            control = new SupplierAutoSuggestBox
            {
                ItemsSource = new ObservableCollection<SupplierDto> { supplier },
                SelectionCommittedCommand = committed,
                SelectionCommittedCommandParameter = context,
            };

            using var host = new ControlHost(control);
            await PumpAsync();
            var editor = GetEditor(control);
            var input = GetInput(editor);
            input.Text = "S";
            await PumpAsync();
            var list = GetSuggestionList(editor);
            list.UpdateLayout();
            var item = (global::Wpf.Ui.Controls.ListViewItem?)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(item);
            list.SelectedItem = editor.Items[0];
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseUpEvent,
                Source = item,
            });
            await PumpAsync();

            Assert.AreSame(supplier, control.SelectedSupplier);
            Assert.AreEqual(1, committed.Count);
            Assert.AreSame(context, committed.Parameter);
        });
    }

    /// <summary>
    /// 验证三个业务控件使用各自规定的单字段显示，并允许外部回填缓存之外的完整 DTO。
    /// </summary>
    [TestMethod]
    public void SpecializedAutoSuggestBoxes_ShouldDisplayExternalTypedSelectionWithoutSubmitting()
    {
        Run(async () =>
        {
            var colorCommand = new RecordingCommand();
            var customerCommand = new RecordingCommand();
            var supplierCommand = new RecordingCommand();
            var color = new ProductColorDto { Uid = Guid.NewGuid(), ColorNameZh = "深蓝", ColorCode = "BL" };
            var customer = Customer("C100", "缓存外客户");
            var supplier = Supplier("S100", "缓存外供应商");
            var colorControl = new ProductColorAutoSuggestBox { SelectedColor = color, SelectionCommittedCommand = colorCommand };
            var customerControl = new CustomerAutoSuggestBox { SelectedCustomer = customer, SelectionCommittedCommand = customerCommand };
            var supplierControl = new SupplierAutoSuggestBox { SelectedSupplier = supplier, SelectionCommittedCommand = supplierCommand };
            var panel = new StackPanel();
            panel.Children.Add(colorControl);
            panel.Children.Add(customerControl);
            panel.Children.Add(supplierControl);

            using var host = new ControlHost(panel);
            await PumpAsync();
            Assert.AreEqual("深蓝", GetInput(GetEditor(colorControl)).Text);
            Assert.AreEqual("C100", GetInput(GetEditor(customerControl)).Text);
            Assert.AreEqual("S100", GetInput(GetEditor(supplierControl)).Text);
            Assert.AreEqual(0, colorCommand.Count);
            Assert.AreEqual(0, customerCommand.Count);
            Assert.AreEqual(0, supplierCommand.Count);
        });
    }

    /// <summary>
    /// 验证共享缓存刷新只更新当前实例候选，不自动打开弹层，也不改变另一实例的筛选。
    /// </summary>
    [TestMethod]
    public void CacheRefreshAndMultipleInstances_ShouldKeepSuggestionStateIsolated()
    {
        Run(async () =>
        {
            var source = new ObservableCollection<CustomerDto>
            {
                Customer("A001", "甲"),
                Customer("B001", "乙"),
            };
            var first = new CustomerAutoSuggestBox { ItemsSource = source };
            var second = new CustomerAutoSuggestBox { ItemsSource = source };
            var panel = new StackPanel();
            panel.Children.Add(first);
            panel.Children.Add(second);

            using var host = new ControlHost(panel);
            await PumpAsync();
            GetInput(GetEditor(first)).Text = "A";
            GetInput(GetEditor(second)).Text = "B";
            await PumpAsync();
            first.IsSuggestionListOpen = false;
            second.IsSuggestionListOpen = false;

            source.Add(Customer("A002", "丙"));
            await PumpAsync();
            Assert.AreEqual(3, source.Count);
            Assert.AreEqual(2, GetEditor(first).Items.Count);
            Assert.AreEqual(1, GetEditor(second).Items.Count);
            Assert.IsFalse(first.IsSuggestionListOpen);
            Assert.IsFalse(second.IsSuggestionListOpen);
        });
    }

    /// <summary>
    /// 验证标准 FontSize 的固定值和运行时修改传递到输入框及候选主要、辅助文字。
    /// </summary>
    [TestMethod]
    public void FontSize_ShouldReachInputAndSuggestionTextAtRuntime()
    {
        Run(async () =>
        {
            var control = new CustomerAutoSuggestBox
            {
                FontSize = 17,
                ItemsSource = new ObservableCollection<CustomerDto> { Customer("C001", "甲客户") },
            };
            using var host = new ControlHost(control);
            await PumpAsync();
            var editor = GetEditor(control);
            var input = GetInput(editor);
            input.Text = "C";
            await PumpAsync();
            var list = GetSuggestionList(editor);
            list.UpdateLayout();
            var item = (global::Wpf.Ui.Controls.ListViewItem?)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(item);
            var texts = FindVisualDescendants<TextBlock>(item).ToArray();
            Assert.AreEqual(2, texts.Length);
            Assert.AreEqual(17d, editor.FontSize);
            Assert.AreEqual(17d, input.FontSize);
            Assert.IsTrue(texts.All(text => text.FontSize == 17d));

            control.FontSize = 21;
            await PumpAsync();
            Assert.AreEqual(21d, editor.FontSize);
            Assert.AreEqual(21d, input.FontSize);
            Assert.IsTrue(texts.All(text => text.FontSize == 21d));
        });
    }

    /// <summary>
    /// 验证 Esc 只放弃当前搜索并恢复已提交对象，不清空选择或执行命令。
    /// </summary>
    [TestMethod]
    public void Escape_ShouldRestoreCommittedDisplayWithoutBusinessNotification()
    {
        Run(async () =>
        {
            var customer = Customer("C001", "甲客户");
            var committed = new RecordingCommand();
            var cleared = new RecordingCommand();
            var control = new CustomerAutoSuggestBox
            {
                ItemsSource = new ObservableCollection<CustomerDto> { customer },
                SelectedCustomer = customer,
                SelectionCommittedCommand = committed,
                SelectionClearedCommand = cleared,
            };
            using var host = new ControlHost(control);
            await PumpAsync();
            var editor = GetEditor(control);
            var input = GetInput(editor);
            control.FocusEditor();
            input.Text = "pending";
            await PumpAsync();
            Assert.IsTrue(control.IsSuggestionListOpen);

            RaisePreviewKey(input, Key.Escape, editor);
            await PumpAsync();
            Assert.AreSame(customer, control.SelectedCustomer);
            Assert.AreEqual("C001", input.Text);
            Assert.AreEqual(string.Empty, control.SearchText);
            Assert.IsFalse(control.IsSuggestionListOpen);
            Assert.AreEqual(0, committed.Count);
            Assert.AreEqual(0, cleared.Count);
        });
    }

    /// <summary>
    /// 验证内部提交使用 SetCurrentValue，强类型双向 Binding 不会被选择操作替换。
    /// </summary>
    [TestMethod]
    public void TypedSelection_ShouldKeepTwoWayBindingAndUpdateItsSource()
    {
        Run(async () =>
        {
            var first = Customer("C001", "甲客户");
            var second = Customer("C002", "乙客户");
            var source = new CustomerSelectionSource { Selection = first };
            var control = new CustomerAutoSuggestBox
            {
                ItemsSource = new ObservableCollection<CustomerDto> { first, second },
            };
            BindingOperations.SetBinding(
                control,
                CustomerAutoSuggestBox.SelectedCustomerProperty,
                new Binding(nameof(CustomerSelectionSource.Selection))
                {
                    Source = source,
                    Mode = BindingMode.TwoWay,
                });

            using var host = new ControlHost(control);
            await PumpAsync();
            var editor = GetEditor(control);
            GetInput(editor).Text = "C002";
            await PumpAsync();
            var list = GetSuggestionList(editor);
            list.UpdateLayout();
            list.SelectedItem = editor.Items[0];
            var item = (global::Wpf.Ui.Controls.ListViewItem?)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(item);
            item.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseUpEvent,
                Source = item,
            });
            await PumpAsync();

            Assert.AreSame(second, source.Selection);
            Assert.AreSame(second, control.SelectedCustomer);
            Assert.IsTrue(BindingOperations.IsDataBound(control, CustomerAutoSuggestBox.SelectedCustomerProperty));
        });
    }

    /// <summary>
    /// 验证外部清空 SearchText 只同步显示，只有用户删除或点击清空按钮才解除已选对象。
    /// </summary>
    [TestMethod]
    public void ProgrammaticAndUserClear_ShouldHaveDistinctSelectionSemantics()
    {
        Run(async () =>
        {
            var customer = Customer("C001", "甲客户");
            var cleared = new RecordingCommand();
            var reasons = new List<AutoSuggestionBoxTextChangeReason>();
            var control = new CustomerAutoSuggestBox
            {
                ItemsSource = new ObservableCollection<CustomerDto> { customer },
                SelectedCustomer = customer,
                SelectionClearedCommand = cleared,
            };
            using var host = new ControlHost(control);
            await PumpAsync();
            var editor = GetEditor(control);
            var input = GetInput(editor);
            editor.TextChanged += (_, args) => reasons.Add(args.Reason);

            control.FocusEditor();
            control.SearchText = "external";
            control.SearchText = string.Empty;
            await PumpAsync();
            Assert.AreSame(customer, control.SelectedCustomer);
            Assert.AreEqual(0, cleared.Count);
            CollectionAssert.Contains(reasons, AutoSuggestionBoxTextChangeReason.ProgrammaticChange);

            RaiseUserTextChange(editor, "typed");
            RaiseUserTextChange(editor, string.Empty);
            await PumpAsync();
            Assert.IsNull(control.SelectedCustomer);
            Assert.AreEqual(1, cleared.Count);

            control.SelectedCustomer = customer;
            control.FocusEditor();
            input.Text = "again";
            await PumpAsync();
            input.UpdateLayout();
            var clearButton = FindVisualDescendants<Button>(input).Single(button => button.IsVisible);
            var invokeProvider = (IInvokeProvider)new ButtonAutomationPeer(clearButton)
                .GetPattern(PatternInterface.Invoke);
            invokeProvider.Invoke();
            await PumpAsync();
            Assert.IsNull(control.SelectedCustomer);
            Assert.AreEqual(2, cleared.Count);
        });
    }

    private static CustomerDto Customer(string code, string name) => new()
    {
        Uid = Guid.NewGuid(),
        CustomerCode = code,
        CustomerName = name,
    };

    private static SupplierDto Supplier(string code, string name) => new()
    {
        Uid = Guid.NewGuid(),
        SupplierCode = code,
        SupplierName = name,
    };

    private static CommittedAutoSuggestBox GetEditor(FrameworkElement control) =>
        (CommittedAutoSuggestBox)(control.FindName("SuggestEditor")
            ?? throw new AssertFailedException($"{control.GetType().Name} 缺少内部 SuggestEditor。"));

    private static WpfTextBox GetInput(CommittedAutoSuggestBox editor)
    {
        editor.ApplyTemplate();
        return (WpfTextBox)(editor.Template.FindName("PART_TextBox", editor)
            ?? throw new AssertFailedException("AutoSuggestBox 缺少 PART_TextBox。"));
    }

    private static global::Wpf.Ui.Controls.ListView GetSuggestionList(CommittedAutoSuggestBox editor)
    {
        editor.ApplyTemplate();
        return (global::Wpf.Ui.Controls.ListView)(editor.Template.FindName("PART_SuggestionsList", editor)
            ?? throw new AssertFailedException("AutoSuggestBox 缺少 PART_SuggestionsList。"));
    }

    private static void RaisePreviewKey(UIElement target, Key key, Visual? sourceFallback = null)
    {
        var source = PresentationSource.FromVisual(target)
                     ?? (sourceFallback is null ? null : PresentationSource.FromVisual(sourceFallback))
                     ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    /// <summary>
    /// 通过 WPF-UI 的受保护入口模拟其已判定为用户编辑的文字变化，避免把测试代码赋值误当成键盘输入。
    /// </summary>
    private static void RaiseUserTextChange(CommittedAutoSuggestBox editor, string text)
    {
        var method = typeof(global::Wpf.Ui.Controls.AutoSuggestBox).GetMethod(
            "OnTextChanged",
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            [typeof(AutoSuggestionBoxTextChangeReason), typeof(string)],
            null);
        Assert.IsNotNull(method, "WPF-UI 4.3.0 应提供受保护的 OnTextChanged(reason, text) 入口。");
        method.Invoke(editor, [AutoSuggestionBoxTextChangeReason.UserInput, text]);
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualDescendants<T>(child))
            {
                yield return descendant;
            }
        }
    }

    private static async Task PumpAsync() =>
        await Dispatcher.CurrentDispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background);

    private static void Run(Func<Task> action) => StaTest.RunAsync(action);

    /// <summary>
    /// 提供真实窗口、焦点目标和确定的释放边界。
    /// </summary>
    private sealed class ControlHost : IDisposable
    {
        private readonly Window _window;

        public ControlHost(UIElement control)
        {
            Next = new Button { Content = "下一项" };
            var panel = new StackPanel();
            panel.Children.Add(control);
            panel.Children.Add(Next);
            _window = new Window
            {
                Width = 520,
                Height = 240,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
            };
            _window.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
            _window.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
            _window.Content = panel;
            _window.Show();
            _window.Activate();
        }

        public Button Next { get; }

        public void Dispose()
        {
            foreach (var editor in FindVisualDescendants<CommittedAutoSuggestBox>(_window))
            {
                editor.IsSuggestionListOpen = false;
            }

            _window.Close();
        }
    }

    /// <summary>
    /// 记录命令次数与最后参数，不执行业务操作。
    /// </summary>
    private class RecordingCommand : ICommand
    {
        public int Count { get; private set; }

        public object? Parameter { get; private set; }

        public virtual bool CanExecute(object? parameter) => true;

        public virtual void Execute(object? parameter)
        {
            Count++;
            Parameter = parameter;
        }

        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }

    /// <summary>
    /// 在记录命令前验证强类型选择已完成回写。
    /// </summary>
    private sealed class CallbackCommand(Action callback) : RecordingCommand
    {
        public override void Execute(object? parameter)
        {
            callback();
            base.Execute(parameter);
        }
    }

    /// <summary>
    /// 提供真实的双向依赖属性绑定源。
    /// </summary>
    private sealed class CustomerSelectionSource : DependencyObject
    {
        public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
            nameof(Selection),
            typeof(CustomerDto),
            typeof(CustomerSelectionSource),
            new PropertyMetadata(null));

        public CustomerDto? Selection
        {
            get => (CustomerDto?)GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }
    }
}

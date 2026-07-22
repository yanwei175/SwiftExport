using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

[TestClass]
public sealed class LookupTextBoxTests
{
    [TestMethod]
    public void ClearButton_ShouldBeUniqueAndClearLookupStateWithoutSearching()
    {
        StaTest.RunAsync(async () =>
        {
            var item = new LookupItem("A");
            var searchCommand = new RecordingCommand();
            var clearedCommand = new RecordingCommand();
            var clearedParameter = new object();
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Remote,
                SearchCommand = searchCommand,
                SearchDelay = TimeSpan.FromMilliseconds(30),
                MinimumSearchLength = 1,
                ItemsSource = new[] { item },
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValuePath = nameof(LookupItem.Name),
                SelectedItem = item,
                SelectionClearedCommand = clearedCommand,
                SelectionClearedCommandParameter = clearedParameter,
            };
            var window = new Window
            {
                Width = 320,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup
            };

            try
            {
                window.Show();
                lookup.UpdateLayout();

                var input = (TextBox?)lookup.FindName("InputTextBox");
                var clearButton = (Button?)lookup.FindName("BtnClear");
                var displayText = (TextBlock?)lookup.FindName("DisplayTextBlock");
                Assert.IsNotNull(input);
                Assert.IsNotNull(clearButton);
                Assert.IsNotNull(displayText);

                input.ApplyTemplate();
                Assert.IsNotNull(
                    input.Template.FindName("PART_ContentHost", input),
                    "内部输入框必须保留 WPF TextBox 的 PART_ContentHost。");
                Assert.AreEqual(
                    0,
                    FindVisualDescendants<Button>(input).Count(),
                    "内部输入框模板不得包含 Fluent ClearButton 或其他按钮。");
                Assert.AreEqual(
                    1,
                    FindVisualDescendants<Button>(lookup).Count(button => button.IsVisible),
                    "LookupTextBox 有内容时只能显示自身的一个清除按钮。");

                lookup.IsDropDownOpen = true;
                clearButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(80);

                Assert.AreEqual(string.Empty, lookup.Text);
                Assert.IsNull(lookup.SelectedItem);
                Assert.IsNull(lookup.SelectedValue);
                Assert.AreEqual(string.Empty, input.Text);
                Assert.AreEqual(string.Empty, displayText.Text);
                Assert.IsFalse(lookup.HasTextOrSelection);
                Assert.IsFalse(lookup.IsDropDownOpen);
                Assert.AreEqual(0, searchCommand.ExecutionCount, "清除操作不得触发远程搜索。");
                Assert.AreEqual(1, clearedCommand.ExecutionCount, "只有显式点击清除按钮才发布解除关联命令。");
                Assert.AreSame(clearedParameter, clearedCommand.LastParameter);
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [TestMethod]
    public void RemoteSearch_ShouldIgnoreInitializationAndOnlySearchFocusedEdits()
    {
        StaTest.RunAsync(async () =>
        {
            var searchCommand = new RecordingCommand();
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Remote,
                SearchCommand = searchCommand,
                SearchDelay = TimeSpan.FromMilliseconds(30),
                MinimumSearchLength = 1,
                Text = "initial"
            };
            var window = new Window
            {
                Width = 320,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup
            };

            try
            {
                window.Show();
                await Task.Delay(80);
                Assert.AreEqual(0, searchCommand.ExecutionCount, "初始 Text 不应触发远程搜索。");

                lookup.ItemsSource = new[] { new LookupItem("A") };
                Assert.IsFalse(lookup.IsDropDownOpen, "ItemsSource 初始化不得打开 Popup。");
                lookup.SelectedItem = ((LookupItem[])lookup.ItemsSource)[0];
                lookup.Text = "external";
                await Task.Delay(80);
                Assert.AreEqual(0, searchCommand.ExecutionCount, "ItemsSource、SelectedItem 和外部 Text 回填不应触发搜索。");

                lookup.SelectedItem = null;
                var input = (TextBox?)lookup.FindName("InputTextBox");
                Assert.IsNotNull(input);
                window.Activate();
                input.Focus();
                Keyboard.Focus(input);
                await Task.Delay(30);
                Assert.IsTrue(input.IsKeyboardFocusWithin, "测试输入框必须处于真实键盘焦点状态。");
                input.Text = "a";
                input.Text = "ab";
                input.Text = "abc";
                await WaitForDispatcherAsync(80);

                Assert.AreEqual(1, searchCommand.ExecutionCount, "快速连续输入应合并为一次远程搜索。");
                Assert.AreEqual("abc", searchCommand.LastParameter);
                Assert.IsFalse(lookup.IsDropDownOpen, "远程命令执行本身不得在结果返回前打开 Popup。");

                lookup.ItemsSource = new[] { new LookupItem("ABC Result") };
                await Task.Delay(20);
                Assert.IsTrue(lookup.IsDropDownOpen, "用户输入后的远程结果替换应打开 Popup。");

                lookup.MinimumSearchLength = 5;
                input.Text = "four";
                await WaitForDispatcherAsync(80);
                Assert.AreEqual(1, searchCommand.ExecutionCount, "不足最小搜索长度时不得执行搜索。");
                Assert.IsFalse(lookup.IsDropDownOpen, "不足最小搜索长度时应关闭 Popup。");

                lookup.MinimumSearchLength = 1;
                input.Text = "cancelled";
                window.Close();
                await Task.Delay(80);
                Assert.AreEqual(1, searchCommand.ExecutionCount, "Unloaded 应取消待执行搜索。");
            }
            finally
            {
                if (window.IsVisible)
                {
                    window.Close();
                }
            }
        });
    }

    [TestMethod]
    public void LocalLookup_InitializationAndFocus_ShouldNeverOpenDropDown()
    {
        StaTest.RunAsync(async () =>
        {
            var items = new ObservableCollection<LookupItem> { new("Alpha") };
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                SearchDelay = TimeSpan.FromMilliseconds(20),
                DisplayMemberPath = nameof(LookupItem.Name),
            };
            var everOpened = false;
            var descriptor = DependencyPropertyDescriptor.FromProperty(
                LookupTextBox.IsDropDownOpenProperty,
                typeof(LookupTextBox));
            EventHandler changed = (_, _) => everOpened |= lookup.IsDropDownOpen;
            descriptor.AddValueChanged(lookup, changed);
            lookup.ItemsSource = items;

            var window = new Window
            {
                Width = 320,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                await Task.Delay(30);
                Assert.IsFalse(lookup.IsDropDownOpen, "首次显示不得打开 Popup。");

                var input = (TextBox?)lookup.FindName("InputTextBox");
                Assert.IsNotNull(input);
                window.Activate();
                input.Focus();
                Keyboard.Focus(input);
                await Task.Delay(30);
                Assert.IsFalse(lookup.IsDropDownOpen, "鼠标或键盘聚焦不得打开 Popup。");

                items.Add(new LookupItem("Beta"));
                await Task.Delay(30);
                Assert.IsFalse(lookup.IsDropDownOpen, "空文本聚焦时集合变化不得打开 Popup。");

                lookup.FocusEditor();
                await Task.Delay(30);
                Assert.IsFalse(lookup.IsDropDownOpen, "FocusEditor 只能聚焦编辑器。");
                Assert.IsFalse(everOpened, "初始化和聚焦过程中不得出现瞬时打开记录。");
            }
            finally
            {
                descriptor.RemoveValueChanged(lookup, changed);
                window.Close();
            }
        });
    }

    [TestMethod]
    public void FocusEditor_WithCommittedSelection_ShouldShowEditableTextAndPreserveSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var selected = new LookupItem("Alpha");
            var searchCommand = new RecordingCommand();
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Remote,
                SearchCommand = searchCommand,
                ItemsSource = new[] { selected },
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValuePath = nameof(LookupItem.Name),
                SelectedItem = selected,
                ClearSelectionOnEdit = false,
            };
            var window = new Window
            {
                Width = 320,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var input = (TextBox?)lookup.FindName("InputTextBox");
                var displayBorder = (Border?)lookup.FindName("DisplayTextBlockBorder");
                Assert.IsNotNull(input);
                Assert.IsNotNull(displayBorder);
                Assert.AreEqual(Visibility.Collapsed, input.Visibility);

                lookup.FocusEditor();
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.AreEqual(Visibility.Visible, input.Visibility);
                Assert.AreEqual(Visibility.Collapsed, displayBorder.Visibility);
                Assert.AreEqual("Alpha", input.Text);
                Assert.AreEqual(input.Text.Length, input.SelectionLength);
                Assert.AreSame(selected, lookup.SelectedItem);
                Assert.AreEqual(0, searchCommand.ExecutionCount);
                Assert.IsFalse(lookup.IsDropDownOpen);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void SelectedLookup_DefaultEditBehavior_ShouldClearCommittedSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var selected = new LookupItem("Alpha");
            var lookup = new LookupTextBox
            {
                ItemsSource = new[] { selected },
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValuePath = nameof(LookupItem.Name),
                SelectedItem = selected,
            };
            var window = new Window
            {
                Width = 320,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                var editorBorder = (Border?)lookup.FindName("EditorBorder");
                Assert.IsNotNull(editorBorder);
                Assert.IsTrue(lookup.ClearSelectionOnEdit, "默认值必须兼容既有的进入编辑即清空行为。");

                RaiseMouseLeftButtonDown(editorBorder);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsNull(lookup.SelectedItem);
                Assert.IsNull(lookup.SelectedValue);
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void SelectedLookup_PreserveMode_ShouldRestoreSelectionAfterEscapeOrLostFocus()
    {
        StaTest.RunAsync(async () =>
        {
            var selected = new LookupItem("Alpha");
            var clearedCommand = new RecordingCommand();
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                SearchDelay = TimeSpan.FromMilliseconds(20),
                ItemsSource = new[] { selected, new LookupItem("Beta") },
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValuePath = nameof(LookupItem.Name),
                SelectedItem = selected,
                ClearSelectionOnEdit = false,
                SelectionClearedCommand = clearedCommand,
            };
            var nextControl = new Button { Content = "Next" };
            var panel = new StackPanel();
            panel.Children.Add(lookup);
            panel.Children.Add(nextControl);
            var window = new Window
            {
                Width = 320,
                Height = 160,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = panel,
            };

            try
            {
                window.Show();
                window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                var editorBorder = (Border?)lookup.FindName("EditorBorder");
                var input = (TextBox?)lookup.FindName("InputTextBox");
                var displayBorder = (Border?)lookup.FindName("DisplayTextBlockBorder");
                var displayText = (TextBlock?)lookup.FindName("DisplayTextBlock");
                Assert.IsNotNull(editorBorder);
                Assert.IsNotNull(input);
                Assert.IsNotNull(displayBorder);
                Assert.IsNotNull(displayText);

                RaiseMouseLeftButtonDown(editorBorder);
                await Dispatcher.Yield(DispatcherPriority.Input);
                Assert.AreSame(selected, lookup.SelectedItem);
                Assert.AreEqual("Alpha", lookup.SelectedValue);

                RaisePreviewKey(input, Key.Down);
                Assert.IsTrue(lookup.IsDropDownOpen);
                Assert.AreSame(selected, lookup.SelectedItem, "只展开候选列表不得清除旧选择。");

                RaisePreviewKey(input, Key.Escape);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsFalse(lookup.IsDropDownOpen);
                Assert.AreSame(selected, lookup.SelectedItem);
                Assert.AreEqual(Visibility.Collapsed, input.Visibility);
                Assert.AreEqual(Visibility.Visible, displayBorder.Visibility);
                Assert.AreEqual("Alpha", displayText.Text);

                RaiseMouseLeftButtonDown(editorBorder);
                await Dispatcher.Yield(DispatcherPriority.Input);
                input.Text = "Bet";
                await WaitForDispatcherAsync(60);
                Assert.AreSame(selected, lookup.SelectedItem, "搜索输入期间旧选择仍应保持为已提交值。");

                nextControl.Focus();
                Keyboard.Focus(nextControl);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.AreSame(selected, lookup.SelectedItem, "放弃候选并失焦后必须保留上一次选择。");
                Assert.AreEqual("Alpha", lookup.SelectedValue);
                Assert.AreEqual(string.Empty, lookup.Text);
                Assert.AreEqual(string.Empty, input.Text);
                Assert.AreEqual(Visibility.Collapsed, input.Visibility);
                Assert.AreEqual(Visibility.Visible, displayBorder.Visibility);
                Assert.AreEqual(0, clearedCommand.ExecutionCount, "只有显式清除按钮才允许发布清除命令。");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void LocalLookup_UserInput_ShouldOpenMatchesAndCancelOnLostFocus()
    {
        StaTest.RunAsync(async () =>
        {
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                SearchDelay = TimeSpan.FromMilliseconds(20),
                ItemsSource = new[] { new LookupItem("Alpha"), new LookupItem("Beta") },
                DisplayMemberPath = nameof(LookupItem.Name),
            };
            var nextControl = new Button { Content = "Next" };
            var panel = new StackPanel();
            panel.Children.Add(lookup);
            panel.Children.Add(nextControl);
            var window = new Window
            {
                Width = 320,
                Height = 160,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = panel,
            };

            try
            {
                window.Show();
                var input = (TextBox?)lookup.FindName("InputTextBox");
                Assert.IsNotNull(input);
                window.Activate();
                input.Focus();
                Keyboard.Focus(input);

                input.Text = "Al";
                await WaitForDispatcherAsync(60);
                Assert.IsTrue(lookup.IsDropDownOpen, "用户输入后有匹配项应打开 Popup。");
                Assert.IsTrue(input.IsKeyboardFocusWithin, "Popup 打开后输入框必须保持键盘焦点。");

                input.Text = "missing";
                await WaitForDispatcherAsync(60);
                Assert.IsFalse(lookup.IsDropDownOpen, "无匹配项应关闭 Popup。");

                input.Text = "Be";
                nextControl.Focus();
                Keyboard.Focus(nextControl);
                await WaitForDispatcherAsync(60);
                Assert.IsFalse(lookup.IsDropDownOpen, "失焦必须取消本地防抖，不能延迟重开 Popup。");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void LocalLookup_SpacesShouldActAsWildcardsWithoutDebounce()
    {
        StaTest.RunAsync(async () =>
        {
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                SearchDelay = TimeSpan.FromSeconds(30),
                ItemsSource = new[]
                {
                    new LookupItem("Antique Bronze"),
                    new LookupItem("AntBronze"),
                    new LookupItem("Antique Brass"),
                    new LookupItem("Bronze Antique"),
                },
                DisplayMemberPath = nameof(LookupItem.Name),
            };
            var window = new Window
            {
                Width = 320,
                Height = 160,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                window.Activate();
                var input = (TextBox?)lookup.FindName("InputTextBox");
                var resultList = (ListBox?)lookup.FindName("ResultList");
                Assert.IsNotNull(input);
                Assert.IsNotNull(resultList);
                input.Focus();
                Keyboard.Focus(input);

                input.Text = "Ant   Bronze";
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsTrue(lookup.IsDropDownOpen, "本地搜索必须在输入后立即刷新，不能等待 SearchDelay。");
                CollectionAssert.AreEqual(
                    new[] { "Antique Bronze", "AntBronze" },
                    resultList.Items.Cast<LookupItem>().Select(item => item.Name).ToArray());
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void EmptyLookup_SpaceAndDown_ShouldExplicitlyOpenAllCandidates()
    {
        StaTest.RunAsync(async () =>
        {
            var items = new ObservableCollection<LookupItem> { new("Alpha"), new("Beta") };
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                ItemsSource = items,
                DisplayMemberPath = nameof(LookupItem.Name),
            };
            var window = new Window
            {
                Width = 320,
                Height = 120,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                var input = (TextBox?)lookup.FindName("InputTextBox");
                var resultList = (ListBox?)lookup.FindName("ResultList");
                Assert.IsNotNull(input);
                Assert.IsNotNull(resultList);
                window.Activate();
                input.Focus();
                Keyboard.Focus(input);

                RaisePreviewKey(input, Key.Down);
                await Task.Delay(20);
                Assert.IsTrue(lookup.IsDropDownOpen);
                Assert.AreEqual(0, resultList.SelectedIndex);

                lookup.IsDropDownOpen = false;
                resultList.SelectedIndex = -1;
                RaisePreviewKey(input, Key.Space);
                await Task.Delay(20);
                Assert.IsTrue(lookup.IsDropDownOpen);
                Assert.AreEqual(0, resultList.SelectedIndex);

                lookup.IsDropDownOpen = false;
                lookup.IsDropDownOpen = true;
                Assert.IsTrue(lookup.IsDropDownOpen, "外部仍可显式打开 Popup。");
                items.Clear();
                await Task.Delay(20);
                Assert.IsFalse(lookup.IsDropDownOpen, "显式打开后候选清空应关闭 Popup。");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void PopupResults_MouseWheelShouldUseListBoxInternalScrollViewer()
    {
        StaTest.RunAsync(async () =>
        {
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                ItemsSource = Enumerable.Range(1, 60).Select(index => new LookupItem($"Item {index}")),
                DisplayMemberPath = nameof(LookupItem.Name),
            };
            var window = new Window
            {
                Width = 320,
                Height = 180,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = lookup,
            };

            try
            {
                window.Show();
                lookup.IsDropDownOpen = true;
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                lookup.UpdateLayout();

                var resultList = (ListBox?)lookup.FindName("ResultList");
                Assert.IsNotNull(resultList);
                resultList.UpdateLayout();
                Assert.AreEqual(ScrollBarVisibility.Auto, ScrollViewer.GetVerticalScrollBarVisibility(resultList));
                Assert.AreEqual(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(resultList));
                Assert.IsTrue(ScrollViewer.GetCanContentScroll(resultList));

                var scrollViewers = FindVisualDescendants<ScrollViewer>(resultList).ToArray();
                Assert.AreEqual(1, scrollViewers.Length, "Lookup 结果只应使用 ListBox 模板自身的滚动宿主。");
                var scrollViewer = scrollViewers[0];
                Assert.IsTrue(scrollViewer.ScrollableHeight > 0, "大量候选项必须产生可滚动范围。");

                var firstItem = (ListBoxItem?)resultList.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(firstItem);
                var source = PresentationSource.FromVisual(firstItem)
                             ?? throw new InvalidOperationException("Lookup 候选项尚未连接到 PresentationSource。");
                firstItem.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, Environment.TickCount, -120)
                {
                    RoutedEvent = Mouse.MouseWheelEvent,
                });
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsTrue(scrollViewer.VerticalOffset > 0, "向下滚动鼠标滚轮必须推进候选列表偏移。");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private sealed record LookupItem(string Name);

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

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
                     ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private static void RaiseMouseLeftButtonDown(UIElement target)
    {
        target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent,
            Source = target,
        });
    }

    private static async Task WaitForDispatcherAsync(int milliseconds)
    {
        await Task.Delay(milliseconds);
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecutionCount { get; private set; }

        public object? LastParameter { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            ExecutionCount++;
            LastParameter = parameter;
        }
    }
}

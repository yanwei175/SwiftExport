using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Controls;

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
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Remote,
                SearchCommand = searchCommand,
                SearchDelay = TimeSpan.FromMilliseconds(30),
                MinimumSearchLength = 1,
                ItemsSource = new[] { item },
                DisplayMemberPath = nameof(LookupItem.Name),
                SelectedValuePath = nameof(LookupItem.Name),
                SelectedItem = item
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
                await Task.Delay(80);

                Assert.AreEqual(1, searchCommand.ExecutionCount, "快速连续输入应合并为一次远程搜索。");
                Assert.AreEqual("abc", searchCommand.LastParameter);

                lookup.MinimumSearchLength = 5;
                input.Text = "four";
                await Task.Delay(80);
                Assert.AreEqual(1, searchCommand.ExecutionCount, "不足最小搜索长度时不得执行搜索。");

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

using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>在真实 WPF 宿主中验证模板选择器的原生表格编辑、显式提交及宿主冻结边界。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductAttributeProfileLookupTextBoxTests
{
    /// <summary>进入原生表格编辑时聚焦输入；搜索与取消不能直接修改行模板或发布提交命令。</summary>
    [TestMethod]
    public void DataGridEditing_ShouldFocusInputAndKeepBusinessTemplateWhenCancelled()
    {
        StaTest.RunAsync(async () =>
        {
            var profile = Profile("面盆龙头", "BASIN");
            var row = new TestRow { ProfileId = profile.Uid, ProfileName = profile.DataNameZh };
            var committed = new RecordingCommand();
            var cleared = new RecordingCommand();
            var column = CreateLookupColumn(profile, committed, cleared);
            var grid = new DataGrid { AutoGenerateColumns = false, ItemsSource = new[] { row } };
            grid.Columns.Add(column);
            var window = WindowFor(grid);
            try
            {
                window.Show();
                window.Activate();
                PumpDispatcher();
                grid.CurrentCell = new DataGridCellInfo(row, column);
                Assert.IsTrue(grid.BeginEdit());
                PumpDispatcher();
                await Dispatcher.Yield(DispatcherPriority.Loaded);

                var lookup = FindVisualDescendants<ProductAttributeProfileLookupTextBox>(grid).Single();
                var editor = Editor(lookup);
                var input = (TextBox?)editor.FindName("InputTextBox");
                Assert.IsNotNull(input);
                Assert.IsTrue(input.IsKeyboardFocusWithin);
                Assert.AreEqual(row.ProfileName, input.Text);
                Assert.AreEqual(row.ProfileName.Length, input.SelectionLength);
                Assert.IsFalse(editor.IsDropDownOpen);

                input.Text = "未确认的模板搜索";
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.AreEqual(profile.Uid, row.ProfileId);
                Assert.AreEqual(profile.DataNameZh, row.ProfileName);
                Assert.AreSame(profile, lookup.SelectedProfile);
                Assert.AreEqual(0, committed.ExecutionCount);
                Assert.AreEqual(0, cleared.ExecutionCount);

                RaisePreviewKey(input, Key.Escape);
                Assert.IsFalse(editor.IsDropDownOpen);
                Assert.AreSame(profile, lookup.SelectedProfile);
                Assert.IsTrue(grid.CancelEdit());
                Assert.AreEqual(profile.Uid, row.ProfileId);
                Assert.AreEqual(profile.DataNameZh, row.ProfileName);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>英文编码搜索后键盘确认候选，明确清除只传递宿主参数且不修改字典候选。</summary>
    [TestMethod]
    public void KeyboardCommitAndClear_ShouldForwardHostParametersAndPreserveCandidates()
    {
        StaTest.RunAsync(async () =>
        {
            var first = Profile("面盆龙头", "BASIN");
            var second = Profile("厨房龙头", "KITCHEN");
            var committed = new RecordingCommand();
            var cleared = new RecordingCommand();
            var commitParameter = new object();
            var clearParameter = new object();
            var lookup = new ProductAttributeProfileLookupTextBox
            {
                ItemsSource = new[] { first, second },
                SelectedProfile = first,
                SelectionCommittedCommand = committed,
                SelectionCommittedCommandParameter = commitParameter,
                SelectionClearedCommand = cleared,
                SelectionClearedCommandParameter = clearParameter,
            };
            var window = WindowFor(lookup);
            try
            {
                window.Show();
                window.Activate();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = Editor(lookup);
                editor.SearchDelay = TimeSpan.FromMilliseconds(20);
                lookup.FocusEditor();
                await Dispatcher.Yield(DispatcherPriority.Input);
                var input = (TextBox)editor.FindName("InputTextBox");
                var results = (ListBox)editor.FindName("ResultList");
                input.Text = "KITCH";
                await Task.Delay(80);
                await Dispatcher.Yield(DispatcherPriority.Background);

                Assert.IsTrue(editor.IsDropDownOpen);
                Assert.AreEqual(1, results.Items.Count);
                Assert.AreSame(second, results.Items[0]);
                Assert.AreSame(first, lookup.SelectedProfile, "搜索期间仍保留上一次已确认候选。");
                Assert.AreEqual(0, committed.ExecutionCount);

                results.SelectedItem = second;
                RaisePreviewKey(input, Key.Enter);
                Assert.AreSame(second, lookup.SelectedProfile);
                Assert.AreEqual(1, committed.ExecutionCount);
                Assert.AreSame(commitParameter, committed.LastParameter);
                Assert.IsFalse(editor.IsDropDownOpen);

                ((Button)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.IsNull(lookup.SelectedProfile);
                Assert.AreEqual(1, cleared.ExecutionCount);
                Assert.AreSame(clearParameter, cleared.LastParameter);
                Assert.AreEqual(1, committed.ExecutionCount);
                Assert.AreEqual("厨房龙头", second.DataNameZh);
                Assert.AreEqual("KITCHEN", second.DataNameEn);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>程序回填不展开候选或执行命令；宿主禁用、字号和无障碍名称传递给真实输入控件。</summary>
    [TestMethod]
    public void ProgrammaticRefillAndHostFreeze_ShouldRemainPassive()
    {
        StaTest.RunAsync(async () =>
        {
            var first = Profile("面盆龙头", "BASIN");
            var second = Profile("厨房龙头", "KITCHEN");
            var committed = new RecordingCommand();
            var cleared = new RecordingCommand();
            var lookup = new ProductAttributeProfileLookupTextBox
            {
                ItemsSource = new[] { first, second },
                SelectedProfile = first,
                FontSize = 17,
                SelectionCommittedCommand = committed,
                SelectionClearedCommand = cleared,
            };
            AutomationProperties.SetName(lookup, "当前产品属性模板");
            var window = WindowFor(lookup);
            try
            {
                window.Show();
                await Dispatcher.Yield(DispatcherPriority.Loaded);
                var editor = Editor(lookup);
                lookup.SelectedProfile = second;
                lookup.ItemsSource = new[] { second };
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.AreSame(second, editor.SelectedItem);
                Assert.IsFalse(editor.IsDropDownOpen);
                Assert.AreEqual(0, committed.ExecutionCount);
                Assert.AreEqual(0, cleared.ExecutionCount);
                Assert.AreEqual(17d, editor.FontSize);
                Assert.AreEqual("当前产品属性模板", AutomationProperties.GetName(editor));

                lookup.IsEnabled = false;
                Assert.IsFalse(editor.IsEnabled);
                Assert.IsFalse(((TextBox)editor.FindName("InputTextBox")).IsEnabled);
                Assert.IsFalse(((Button)editor.FindName("BtnClear")).IsEnabled);
                Assert.AreSame(second, lookup.SelectedProfile);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static SysDictDataDto Profile(string name, string code) =>
        new() { Uid = Guid.NewGuid(), DataNameZh = name, DataNameEn = code, IsEnabled = true };

    /// <summary>从已加载的真实视觉树取内部编辑器，避免 DataTemplate 创建的外层名称作用域遮蔽控件名称。</summary>
    private static LookupTextBox Editor(ProductAttributeProfileLookupTextBox lookup) =>
        FindVisualDescendants<LookupTextBox>(lookup).Single();

    private static Window WindowFor(UIElement content) =>
        new() { Width = 480, Height = 200, ShowInTaskbar = false, WindowStyle = WindowStyle.None, Content = content };

    /// <summary>使用原生编辑模板绑定，验证搜索文字不会绕过显式宿主命令写入业务行。</summary>
    private static DataGridTemplateColumn CreateLookupColumn(SysDictDataDto profile, ICommand committed, ICommand cleared)
    {
#pragma warning disable CS0618
        var display = new FrameworkElementFactory(typeof(TextBlock));
        display.SetBinding(TextBlock.TextProperty, new Binding(nameof(TestRow.ProfileName)));
        var edit = new FrameworkElementFactory(typeof(ProductAttributeProfileLookupTextBox));
        edit.SetBinding(ProductAttributeProfileLookupTextBox.TextProperty,
            new Binding(nameof(TestRow.ProfileName)) { Mode = BindingMode.OneWay });
        edit.SetValue(ProductAttributeProfileLookupTextBox.ItemsSourceProperty, new[] { profile });
        edit.SetValue(ProductAttributeProfileLookupTextBox.SelectedProfileProperty, profile);
        edit.SetValue(ProductAttributeProfileLookupTextBox.SelectionCommittedCommandProperty, committed);
        edit.SetValue(ProductAttributeProfileLookupTextBox.SelectionClearedCommandProperty, cleared);
        return new DataGridTemplateColumn
        {
            CellTemplate = new DataTemplate { VisualTree = display },
            CellEditingTemplate = new DataTemplate { VisualTree = edit },
        };
#pragma warning restore CS0618
    }

    private static IEnumerable<T> FindVisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in FindVisualDescendants<T>(child)) yield return descendant;
        }
    }

    private static void PumpDispatcher() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>将真实键盘路由送入已连接宿主的输入框，覆盖内部候选确认和取消行为。</summary>
    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
            ?? throw new InvalidOperationException("测试控件尚未连接到视觉宿主。");
        target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        });
    }

    private sealed class TestRow
    {
        public Guid? ProfileId { get; set; }
        public string ProfileName { get; set; } = string.Empty;
    }

    /// <summary>记录由真实控件交互发出的提交结果，不模拟业务字段回写。</summary>
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

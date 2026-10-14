using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Controls;

/// <summary>通过真实弹层与绑定验证分类树多选，不连接 API 或修改共享缓存。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductCategoryMutiSelectButtonTests
{
    /// <summary>未分类与分类在同一弹层暂存，搜索可找到特殊项，取消和清除仍遵循确定后提交。</summary>
    [TestMethod]
    public void Uncategorized_ShouldShareSearchConfirmCancelAndClearTransaction() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture(showUncategorized: true);
        await fixture.OpenAsync();
        OptionCheck(fixture.Tree, "未分类").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Assert.AreEqual(0, fixture.Selected.Count);
        await fixture.CloseAsync(() => fixture.Popup.IsOpen = false);
        Assert.AreEqual(0, fixture.Selected.Count);
        await fixture.OpenAsync();
        Assert.AreEqual(false, OptionCheck(fixture.Tree, "未分类").IsChecked);
        OptionCheck(fixture.Tree, "未分类").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        OptionCheck(fixture.Tree, "卫浴").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        fixture.Search.Text = "未分类"; await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual(1, fixture.Tree.Items.Count);
        await fixture.CloseAsync(() => fixture.Button("确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        CollectionAssert.AreEqual(new[] { Guid.Empty, fixture.Root.Uid }, fixture.Selected.Select(item => item.Uid).ToArray());
        Assert.AreEqual("分类(2)", fixture.Picker.DisplayText);
        Assert.IsTrue(fixture.Roots.All(item => item.Uid != Guid.Empty), "特殊项不能写入缓存来源。");
        await fixture.OpenAsync();
        fixture.Button("清除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(2, fixture.Selected.Count);
        await fixture.CloseAsync(() => fixture.Popup.IsOpen = false);
        Assert.AreEqual(2, fixture.Selected.Count);
        await fixture.OpenAsync();
        fixture.Button("清除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await fixture.CloseAsync(() => fixture.Button("确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.AreEqual(0, fixture.Selected.Count);
    });

    /// <summary>候选刷新保留未分类草稿，卸载后不继续订阅，重新加载仍只提供一个特殊项。</summary>
    [TestMethod]
    public void Uncategorized_ShouldSurviveSourceRefreshAndReload() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture(showUncategorized: true);
        await fixture.OpenAsync();
        OptionCheck(fixture.Tree, "未分类").SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        fixture.Roots.Add(Category("新增分类", "NEW"));
        await Dispatcher.Yield(DispatcherPriority.DataBind); fixture.Tree.UpdateLayout();
        Assert.AreEqual(true, OptionCheck(fixture.Tree, "未分类").IsChecked);
        await fixture.CloseAsync(() => fixture.Button("确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.AreEqual(Guid.Empty, fixture.Selected.Single().Uid);
        await fixture.OpenAsync();
        await fixture.CloseAsync(() => fixture.Window.Content = null);
        fixture.Roots.Clear(); fixture.Window.Content = fixture.Control;
        await fixture.OpenAsync();
        Assert.AreEqual(1, fixture.Tree.Items.Count);
        Assert.AreEqual(true, OptionCheck(fixture.Tree, "未分类").IsChecked);
    });

    /// <summary>按可访问中文名称定位真实选项，避免把后代勾选框当作当前节点。</summary>
    private static CheckBox OptionCheck(TreeView tree, string name) => Descendants(tree).OfType<CheckBox>()
        .Single(check => AutomationProperties.GetName(check) == name);

    /// <summary>真实树节点显示字符串或业务模板时，从包装项读取模板且不产生 ContentTemplate 路径错误。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void ShouldResolveContentTemplateWithoutBindingErrors(bool useTemplate) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        using var output = new StringWriter();
        using var listener = new TextWriterTraceListener(output);
        var trace = PresentationTraceSources.DataBindingSource;
        var previousLevel = trace.Switch.Level;
        trace.Switch.Level = SourceLevels.Error;
        trace.Listeners.Add(listener);
        try
        {
            await using var fixture = new Fixture();
            if (useTemplate)
                fixture.Picker.ItemTemplate = (DataTemplate)XamlReader.Parse("""
                    <DataTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation">
                        <TextBlock Text="{Binding CategoryCode}" />
                    </DataTemplate>
                    """);
            await fixture.OpenAsync();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            // Trace 开关可能被测试宿主延迟初始化；同时核对实际绑定状态，确保字符串场景可稳定复现。
            var templateBindings = Descendants(fixture.RootRow()).OfType<ContentPresenter>()
                .Select(presenter => presenter.GetBindingExpression(ContentPresenter.ContentTemplateProperty))
                .Where(binding => binding is not null).ToArray();
            Assert.IsNotEmpty(templateBindings);
            foreach (var binding in templateBindings)
                Assert.AreEqual(BindingStatus.Active, binding!.Status, "模板必须绑定包装项，不能在显示内容上查找属性。");
            var expectedText = useTemplate ? fixture.Root.CategoryCode : fixture.Root.FakeName;
            Assert.IsTrue(Descendants(fixture.RootRow()).OfType<TextBlock>().Any(text => text.Text == expectedText));
            listener.Flush();
            var bindingErrors = output.ToString();
            Assert.IsFalse(bindingErrors.Contains("ContentTemplate", StringComparison.Ordinal), bindingErrors);
            Assert.IsFalse(bindingErrors.Contains("DisplayContent", StringComparison.Ordinal), bindingErrors);
        }
        finally
        {
            trace.Listeners.Remove(listener);
            trace.Switch.Level = previousLevel;
        }
    });

    /// <summary>展示中文层级，父子独立勾选，确定后回写缓存对象并执行一次命令。</summary>
    [TestMethod]
    public void ShouldDisplayFakeNamesAndCommitIndependentSelections() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture();
        var commits = 0;
        var parameter = new object();
        fixture.Control.SelectionCommittedCommandParameter = parameter;
        fixture.Control.SelectionCommittedCommand = new RelayCommand<object?>(value =>
        {
            Assert.AreSame(parameter, value);
            commits++;
        });
        await fixture.OpenAsync();
        Assert.AreEqual(Visibility.Collapsed, ((ListBox)fixture.Picker.FindName("PART_ListBox")).Visibility);
        var root = fixture.RootRow();
        Assert.AreEqual(2, root.Items.Count);
        var child = fixture.ChildRow(0);
        Assert.AreEqual("卫浴", AutomationProperties.GetName(root));
        Assert.AreEqual("花洒", AutomationProperties.GetName(child));
        Assert.IsTrue(Descendants(child).OfType<TextBlock>().Any(text => text.Text == "花洒"));

        Check(root).SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        Assert.AreEqual(false, Check(child).IsChecked, "勾选父分类不能自动勾选子分类。");
        child.IsSelected = true;
        PressKey(fixture.Tree, Key.Space);
        Assert.AreEqual(true, Check(child).IsChecked);
        Assert.AreEqual(0, fixture.Selected.Count, "临时勾选不能提前写回。");
        await fixture.CloseAsync(() => fixture.Button("确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));

        CollectionAssert.AreEqual(new[] { fixture.Root, fixture.Child }, fixture.Selected.ToArray());
        Assert.AreEqual(1, commits);
        Assert.AreEqual("分类(2)", fixture.Picker.DisplayText);
    });

    /// <summary>按中文或分类代码搜索保留祖先，Esc 关闭后丢弃未提交勾选。</summary>
    [TestMethod]
    public void ShouldKeepAncestorsWhenSearchingAndCancelWithEscape() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture();
        fixture.Selected.Add(fixture.Root);
        await fixture.OpenAsync();
        fixture.Search.Text = "SHOWER";
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual(1, fixture.Tree.Items.Count);
        Assert.AreEqual(1, fixture.RootRow().Items.Count);
        Assert.AreEqual("花洒", AutomationProperties.GetName(fixture.ChildRow(0)));
        Check(fixture.ChildRow(0)).SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        fixture.Search.Text = "花洒";
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual(1, fixture.RootRow().Items.Count);
        fixture.Search.Text = "没有匹配的分类";
        Assert.AreEqual(0, fixture.Tree.Items.Count);
        await fixture.CloseAsync(() => PressKey(fixture.Tree, Key.Escape));
        CollectionAssert.AreEqual(new[] { fixture.Root }, fixture.Selected.ToArray());

        await fixture.OpenAsync();
        Assert.AreEqual(false, Check(fixture.ChildRow(0)).IsChecked);
        Assert.AreEqual(true, Check(fixture.RootRow()).IsChecked);
    });

    /// <summary>清除包含搜索隐藏的已选后代，且必须确定才修改外部集合。</summary>
    [TestMethod]
    public void ShouldClearHiddenDescendantsOnlyAfterConfirmation() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture();
        fixture.Selected.Add(fixture.Child);
        await fixture.OpenAsync();
        fixture.Search.Text = "没有匹配的分类";
        fixture.Button("清除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.AreEqual(1, fixture.Selected.Count);
        await fixture.CloseAsync(() => PressKey(fixture.Tree, Key.Enter));
        Assert.AreEqual(0, fixture.Selected.Count);
        Assert.AreEqual("分类", fixture.Picker.DisplayText);
        Assert.IsTrue(((ToggleButton)fixture.Picker.FindName("PART_Button")).IsKeyboardFocused);
    });

    /// <summary>打开时缓存替换按 Uid 保留草稿，外部已选集合变化同步到界面而不执行提交。</summary>
    [TestMethod]
    public void ShouldPreserveDraftAcrossCacheRefreshAndSynchronizeExternalSelection() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture();
        await fixture.OpenAsync();
        Check(fixture.ChildRow(0)).SetCurrentValue(ToggleButton.IsCheckedProperty, true);
        var refreshedRoot = Category("卫浴更新", "ROOT", fixture.Root.Uid);
        var refreshedChild = Category("花洒更新", "SHOWER", fixture.Child.Uid);
        refreshedRoot.AddChild(refreshedChild);
        fixture.Roots[0] = refreshedRoot;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual(true, Check(fixture.ChildRow(0)).IsChecked);
        Assert.AreEqual(false, Check(fixture.RootRow()).IsChecked);
        Assert.AreEqual(0, fixture.Selected.Count);
        await fixture.CloseAsync(() => fixture.Button("确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
        Assert.AreSame(refreshedChild, fixture.Selected.Single());

        await fixture.OpenAsync();
        fixture.Selected.Clear();
        fixture.Selected.Add(refreshedRoot);
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        Assert.AreEqual(true, Check(fixture.RootRow()).IsChecked);
        Assert.AreEqual(false, Check(fixture.ChildRow(0)).IsChecked);
        Assert.AreEqual("分类(1)", fixture.Picker.DisplayText);
    });

    /// <summary>卸载关闭弹层，重新加载后使用新缓存快照，并允许空树打开与清除。</summary>
    [TestMethod]
    public void ShouldCloseOnUnloadAndSupportReloadAndEmptyCache() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture();
        fixture.Selected.Add(fixture.Child);
        await fixture.OpenAsync();
        await fixture.CloseAsync(() => fixture.Window.Content = null);
        fixture.Roots.Clear();
        fixture.Window.Content = fixture.Control;
        await Dispatcher.Yield(DispatcherPriority.Loaded);
        await fixture.OpenAsync();
        Assert.AreEqual(0, fixture.Tree.Items.Count);
        Assert.AreEqual(1, fixture.Selected.Count, "缓存变化不能自动提交业务筛选。");
        await fixture.CloseAsync(() => PressKey(fixture.Tree, Key.Enter));
        Assert.AreEqual(0, fixture.Selected.Count);
    });

    /// <summary>鼠标命中节点正文只切换自身，展开箭头不改变勾选且不会关闭弹层。</summary>
    [TestMethod]
    public void ShouldToggleByMouseWithoutCascadingOrClosingPopup() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        await using var fixture = new Fixture();
        await fixture.OpenAsync();
        var root = fixture.RootRow();
        var child = fixture.ChildRow(0);
        var label = Descendants(root).OfType<TextBlock>().First(text => text.Text == "卫浴");
        var point = label.TranslatePoint(new Point(label.ActualWidth / 2, label.ActualHeight / 2), fixture.Tree);
        var hit = (UIElement)fixture.Tree.InputHitTest(point)!;
        hit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseUpEvent });
        Assert.AreEqual(true, Check(root).IsChecked);
        Assert.AreEqual(false, Check(child).IsChecked);
        Assert.IsTrue(fixture.Popup.IsOpen);
        Assert.AreEqual(0, fixture.Selected.Count);

        var expander = Descendants(root).OfType<ToggleButton>().First(button => button is not CheckBox);
        expander.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
        { RoutedEvent = Mouse.PreviewMouseUpEvent });
        Assert.AreEqual(true, Check(root).IsChecked, "展开箭头不能反转分类勾选。");
    });

    /// <summary>创建只读分类缓存节点，英文名称与中文显示名故意不同。</summary>
    private static ProductCategoryCacheNode Category(string fakeName, string code, Guid? id = null) => new(new ProductCategoryDto
    {
        Uid = id ?? Guid.NewGuid(), CategoryName = $"Long English Name {code}", FakeName = fakeName, CategoryCode = code,
    });

    /// <summary>取得当前节点正文的复选框，不包含展开后代的复选框。</summary>
    private static CheckBox Check(TreeViewItem item) => Descendants(item).OfType<CheckBox>().First();

    /// <summary>向真实控件派发键盘路由，验证树导航与弹层快捷键的协作。</summary>
    private static void PressKey(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(
        Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), Environment.TickCount, key) { RoutedEvent = Keyboard.KeyDownEvent });

    /// <summary>枚举实际呈现的子控件，用于检查可观察的文本与勾选状态。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    /// <summary>拥有测试窗口、缓存树及绑定集合，退出时关闭弹层并释放视觉树。</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        public ProductCategoryCacheNode Root { get; } = Category("卫浴", "ROOT");
        public ProductCategoryCacheNode Child { get; } = Category("花洒", "SHOWER");
        public ObservableCollection<ProductCategoryCacheNode> Roots { get; } = [];
        public ObservableCollection<ProductCategoryCacheNode> Selected { get; } = [];
        public ProductCategoryMutiSelectButton Control { get; }
        public MultiSelectLookupButton Picker => (MultiSelectLookupButton)Control.FindName("CategoryPicker");
        public Popup Popup => (Popup)Picker.FindName("PART_Popup");
        public TreeView Tree => (TreeView)Picker.FindName("PART_TreeView");
        public TextBox Search => (TextBox)Picker.FindName("PART_SearchBox");
        public Window Window { get; }

        /// <summary>加载项目主题，按实际页面的方式绑定缓存树和外部可观察已选集合。</summary>
        public Fixture(bool showUncategorized = false)
        {
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
            Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
            foreach (var resource in new[] { "Variables.xaml", "Typography.xaml", "ListBox.xaml" })
                Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
                { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/{resource}") });
            Control = new ProductCategoryMutiSelectButton { ShowUncategorizedOption = showUncategorized };
            Root.AddChild(Child);
            Root.AddChild(Category("龙头", "TAP"));
            Roots.Add(Root);
            Roots.Add(Category("配件", "PARTS"));
            Control.SetBinding(ProductCategoryMutiSelectButton.ItemsSourceProperty, new Binding { Source = Roots });
            Control.SetBinding(ProductCategoryMutiSelectButton.SelectedItemsProperty, new Binding(nameof(Selected)) { Source = this, Mode = BindingMode.OneWay });
            Window = new Window { Content = Control, Width = 420, Height = 220, ShowInTaskbar = false };
            Window.Show();
            Window.Activate();
        }

        /// <summary>等待布局及弹层加载，确保容器已生成。</summary>
        public async Task OpenAsync()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            Tree.UpdateLayout();
        }

        /// <summary>获取实际树根容器。</summary>
        public TreeViewItem RootRow()
        {
            Tree.UpdateLayout();
            return (TreeViewItem)Tree.ItemContainerGenerator.ContainerFromIndex(0);
        }

        /// <summary>获取实际子分类容器。</summary>
        public TreeViewItem ChildRow(int index)
        {
            var root = RootRow();
            root.UpdateLayout();
            return (TreeViewItem)root.ItemContainerGenerator.ContainerFromIndex(index);
        }

        /// <summary>按用户可见文字定位弹层操作按钮。</summary>
        public Button Button(string text) => Descendants(Popup.Child).OfType<Button>().Single(button => Equals(button.Content, text));

        /// <summary>等待关闭事件和延迟清理完成，防止重开与旧清理相互覆盖。</summary>
        public async Task CloseAsync(Action close)
        {
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            EventHandler handler = (_, _) => closed.TrySetResult();
            Popup.Closed += handler;
            try
            {
                close();
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
                await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            }
            finally { Popup.Closed -= handler; }
        }

        /// <summary>结束测试窗口，触发控件卸载与订阅解除。</summary>
        public async ValueTask DisposeAsync()
        {
            if (Popup.IsOpen) await CloseAsync(() => Popup.IsOpen = false);
            Window.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    }
}

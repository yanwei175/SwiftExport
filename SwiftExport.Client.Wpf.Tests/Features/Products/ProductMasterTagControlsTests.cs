using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExportClient.Features.Products.ProductList.Models;
using SwiftExportClient.Features.Products.ProductList.ViewModels;
using SwiftExportClient.Features.Products.ProductList.Views;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Services.Caching;
using SwiftExportClient.Services.UI;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using SwiftExportClient.Resources.Controls.Filter;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>以真实 STA 多选控件验证本地搜索、取消、确定与清空对详情选择草稿的影响。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductMasterTagControlsTests
{
    /// <summary>按编码本地搜索，关闭不接纳临时勾选，确定与清空更新完整最终身份。</summary>
    [TestMethod]
    public void MultiSelect_ShouldSearchCancelConfirmAndClear() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var definition = ProductMasterTagSelectionTests.Definition();
        using var model = new ProductMasterTagSelectionModel();
        model.RefreshDefinitions(ProductMasterTagSelectionTests.Resolution([definition], true));
        model.Load(ProductMasterTagSelectionTests.FixtureCategoryId, [definition.Options[0].Uid], []);
        var group = model.Groups.Single();
        var control = new MultiSelectLookupButton
        {
            Header = "安装方式", ItemsSource = group.Options, SelectedItems = group.SelectedItems,
            SelectedValuePath = "Uid", DisplayMemberPath = "OptionName", FilterMemberPaths = "OptionName,OptionCode",
            SelectionCommittedCommand = group.CommitMultipleSelectionCommand
        };
        var window = new Window { Content = control, Width = 360, Height = 180, ShowInTaskbar = false };
        var popup = (Popup)control.FindName("PART_Popup");
        try
        {
            window.Show();
            window.Activate();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var search = (TextBox)control.FindName("PART_SearchBox");
            search.Text = "wall";
            await Dispatcher.Yield(DispatcherPriority.Background);
            var list = (ListBox)control.FindName("PART_ListBox");
            Assert.AreEqual(1, list.Items.Count);
            list.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var checkbox = Descendants(row).OfType<CheckBox>().Single();
            checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            await ClosePopupAsync(popup, () => popup.IsOpen = false);
            Assert.IsFalse(model.IsDirty);
            CollectionAssert.AreEqual(new[] { definition.Options[0].Uid }, model.GetSelectedOptionIds()!);

            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            search.Text = "wall";
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.IsTrue(popup.IsOpen, "重开后的弹出层必须保持显示。");
            Assert.AreEqual(1, list.Items.Count);
            list.UpdateLayout();
            row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Assert.IsNotNull(row, "搜索结果必须实现可勾选的行容器。");
            checkbox = Descendants(row).OfType<CheckBox>().Single();
            checkbox.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            await ClosePopupAsync(popup, () => Button(popup, "确定").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
            Assert.IsFalse(popup.IsOpen);
            Assert.AreEqual(2, model.GetSelectedOptionIds()!.Count);
            Assert.IsTrue(model.IsDirty);

            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            Button(popup, "清除").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
            Assert.AreEqual(2, model.GetSelectedOptionIds()!.Count, "弹出层清除必须等待确定。");
            await ClosePopupAsync(popup, () => Button(popup, "确定").RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent)));
            Assert.AreEqual(0, model.GetSelectedOptionIds()!.Count);
        }
        finally
        {
            popup.IsOpen = false;
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
    });

    /// <summary>等待真正 Closed 事件结束旧弹出层，再重开，避免 WPF 延迟关闭清理下一轮候选。</summary>
    private static async Task ClosePopupAsync(Popup popup, Action close)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler handler = (_, _) => closed.TrySetResult();
        popup.Closed += handler;
        try
        {
            close();
            await closed.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        }
        finally { popup.Closed -= handler; }
    }

    /// <summary>以实际标签子视图验证刷新延迟、单选绑定与清除后的最终集合。</summary>
    [TestMethod]
    public void TagsView_ShouldDeferRefreshAndBindSingleChoice() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var definition = ProductMasterTagSelectionTests.Definition();
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, null!, null!, null!, null!, new ProductMasterSyncDialogStub());
        vm.Header.CategoryId = ProductMasterTagSelectionTests.FixtureCategoryId;
        vm.Tags.RefreshDefinitions(ProductMasterTagSelectionTests.Resolution([definition], true));
        vm.Tags.Load(ProductMasterTagSelectionTests.FixtureCategoryId, [definition.Options[0].Uid], []);
        var view = new ProductMasterTagsView { DataContext = vm };
        var window = new Window { Content = view, Width = 760, Height = 400, ShowInTaskbar = false };
        Popup? popup = null;
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var control = Descendants(view).OfType<MultiSelectLookupButton>().Single();
            popup = (Popup)control.FindName("PART_Popup");
            popup.IsOpen = true;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            Assert.IsTrue(vm.Tags.IsPopupOpen);
            var group = vm.Tags.Groups.Single();
            var refreshed = ProductMasterTagSelectionTests.Definition(definition.Uid, definition.Options.Select(item => item.Uid).ToArray());
            refreshed.SelectionMode = ProductAttributeSelectionMode.Single;
            vm.Tags.RefreshDefinitions(ProductMasterTagSelectionTests.Resolution([refreshed], true));
            Assert.AreSame(group, vm.Tags.Groups.Single());
            await ClosePopupAsync(popup, () => popup.IsOpen = false);
            Assert.IsFalse(vm.Tags.IsPopupOpen);
            Assert.IsTrue(vm.Tags.Groups.Single().IsSingle);
            Assert.IsFalse(vm.Tags.IsDirty);
            var combo = Descendants(view).OfType<ComboBox>().Single();
            Assert.AreSame(refreshed.Options[0], combo.SelectedItem);
            combo.SelectedItem = refreshed.Options[1];
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            CollectionAssert.AreEqual(new[] { refreshed.Options[1].Uid }, vm.Tags.GetSelectedOptionIds()!);
            Assert.IsTrue(vm.SaveCommand.CanExecute(null));
            var clear = Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "清除"));
            ((IInvokeProvider)new ButtonAutomationPeer(clear).GetPattern(PatternInterface.Invoke)).Invoke();
            await Dispatcher.Yield(DispatcherPriority.Background);
            Assert.AreEqual(0, vm.Tags.GetSelectedOptionIds()!.Count);
        }
        finally
        {
            if (popup is not null) popup.IsOpen = false;
            window.Close();
            await Dispatcher.Yield(DispatcherPriority.Background);
        }
    });

    /// <summary>实际失效标签按钮服从 Busy、分类确认和定义可用性，等待期间不能修改选择。</summary>
    [TestMethod]
    public void MissingSelectionButton_ShouldBeDisabledWhileBusyOrConfirming() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        LoadResources();
        var completion = new TaskCompletionSource<CacheRefreshResult>();
        var confirmation = new TaskCompletionSource<bool>();
        var cache = Proxy<IReferenceDataCacheService>((method, _) => method.Name switch
        {
            "get_ProductAttributeGroups" => new ReadOnlyObservableCollection<ProductAttributeGroupDto>(new ObservableCollection<ProductAttributeGroupDto>()),
            "get_AreProductAttributesLoaded" => true,
            nameof(IReferenceDataCacheService.ResolveProductAttributes) => ProductMasterTagSelectionTests.Resolution([]),
            "get_ProductCategories" => new ReadOnlyObservableCollection<ProductCategoryCacheNode>(new ObservableCollection<ProductCategoryCacheNode>()),
            nameof(IReferenceDataCacheService.RefreshProductAttributesAsync) => completion.Task,
            _ => null
        });
        var dialogs = Proxy<IDialogService>((method, _) => method.Name == nameof(IDialogService.ConfirmAsync) ? confirmation.Task : null);
        using var vm = new ProductMasterDetailViewModel(null!, null!, null!, cache, Proxy<IMessageService>((_, _) => null), dialogs, null!, new ProductMasterSyncDialogStub());
        var category = Guid.NewGuid();
        vm.Header.Load(new() { Master = new() { MasterSku = "MASTER", CategoryId = category } });
        vm.Tags.Load(category, [Guid.NewGuid()], []);
        vm.Tags.RefreshDefinitions(ProductMasterTagSelectionTests.Resolution([], true));
        var view = new ProductMasterTagsView { DataContext = vm };
        var window = new Window { Content = view, Width = 760, Height = 300, ShowInTaskbar = false };
        try
        {
            window.Show();
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var remove = Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "移除失效标签"));
            Assert.IsTrue(remove.IsEnabled);
            var refreshing = vm.RefreshProductAttributesCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsTrue(vm.IsBusy);
            Assert.IsFalse(remove.IsEnabled);
            completion.SetResult((CacheRefreshResult)Activator.CreateInstance(typeof(CacheRefreshResult), BindingFlags.Instance | BindingFlags.NonPublic,
                null, [false, "测试失败"], null)!);
            await refreshing;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            remove = Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "移除失效标签"));
            Assert.IsTrue(remove.IsEnabled);

            vm.SelectedCategory = (ProductCategoryCacheNode)Activator.CreateInstance(typeof(ProductCategoryCacheNode),
                BindingFlags.Instance | BindingFlags.NonPublic, null, [new ProductCategoryDto { Uid = Guid.NewGuid(), CategoryName = "新分类" }], null)!;
            var changing = vm.CommitCategoryCommand.ExecuteAsync(null);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsTrue(vm.IsCategoryChangePending);
            Assert.IsFalse(remove.IsEnabled);
            Assert.IsFalse(await vm.CanCloseAsync(default));
            confirmation.SetResult(false);
            await changing;
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsTrue(remove.IsEnabled);
            Assert.AreEqual(1, vm.Tags.GetSelectedOptionIds()!.Count);
            vm.Tags.RefreshDefinitions(ProductMasterTagSelectionTests.Resolution([], false));
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            Assert.IsFalse(vm.CanEditTags);
        }
        finally { window.Close(); }
    });

    /// <summary>加载实际业务主题与文本/ListBox资源，确保测试使用产品控件样式。</summary>
    private static void LoadResources()
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        foreach (var resource in new[] { "Variables.xaml", "Typography.xaml", "ListBox.xaml" })
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
            { Source = new Uri($"pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/{resource}") });
    }

    private static Button Button(Popup popup, string text) => Descendants(popup.Child)
        .OfType<Button>().Single(button => Equals(button.Content, text));

    private static T Proxy<T>(Func<MethodInfo, object?[], object?> call) where T : class
    {
        var proxy = DispatchProxy.Create<T, ProductMasterSupplierEditingTests.TestProxy>();
        ((ProductMasterSupplierEditingTests.TestProxy)(object)proxy).InvokeMethod = call;
        return proxy;
    }

    /// <summary>在真实视觉树中寻找已呈现控件，断言绑定行为而非私有实现结构。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

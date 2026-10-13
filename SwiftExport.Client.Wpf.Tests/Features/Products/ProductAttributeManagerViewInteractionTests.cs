using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.Products.Attributes.ViewModels;
using SwiftExportClient.Features.Products.Attributes.Views;
using SwiftExportClient.Resources.Controls.Filter;
using SwiftExportClient.Services.Api;
using SwiftExportClient.Services.UI;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>通过真实主题、原生 Grid 与分类多选弹出层验证独立标签页，不访问生产 API。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductAttributeManagerViewInteractionTests
{
    /// <summary>分别验证组表和值表：数值转换失败阻止关闭，修正后保留原生编辑形成的草稿。</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NativeEdits_BlockInvalidCloseAndPreserveCorrectedDraft(bool editOption) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var view = new ProductAttributeManagerView { DataContext = fixture.ViewModel };
        var window = new Window { Content = view, Width = 1320, Height = 820, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var grid = (DataGrid)view.FindName(editOption ? "OptionGrid" : "GroupGrid");
            var group = fixture.ViewModel.SelectedGroup!;
            var option = fixture.ViewModel.Options.Items.Single();
            object item = editOption ? option : group;
            var sortColumn = grid.Columns[editOption ? 3 : 4];
            grid.CurrentCell = new(item, sortColumn);
            Assert.IsTrue(grid.BeginEdit());
            await Dispatcher.Yield(DispatcherPriority.Background);
            var editor = (TextBox)sortColumn.GetCellContent(item);
            editor.Text = "非法数字"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(Validation.GetHasError(editor));
            Assert.IsFalse(view.CommitPendingEdits());
            Assert.IsFalse(await fixture.ViewModel.CanCloseAsync(default));
            editor.Text = "8"; editor.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            Assert.IsTrue(view.CommitPendingEdits());
            Assert.AreEqual(8, editOption ? option.SortOrder : group.SortOrder);
            Assert.IsTrue(fixture.ViewModel.HasUnsavedChanges);
            Assert.IsFalse(await fixture.ViewModel.CanCloseAsync(default));
            Assert.AreEqual(8, editOption ? option.SortOrder : group.SortOrder);
            Assert.AreEqual(0, fixture.WriteCount);
            Assert.IsTrue(grid.EnableRowVirtualization);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
        Assert.IsNull(fixture.ViewModel.CommitPendingEdits, "卸载后必须解除视觉层提交回调。");
    });

    /// <summary>完整路径搜索只产生直接关联草稿，取消不接纳勾选，确定及清空更新整页变更集。</summary>
    [TestMethod]
    public void CategoryPicker_SearchCancelConfirmAndClearUseDirectRelations() => DatePickerAssistTests.RunWithApplication(async () =>
    {
        ApplicationResourceLoader.Load();
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeAsync();
        var view = new ProductAttributeManagerView { DataContext = fixture.ViewModel };
        var window = new Window { Content = view, Width = 1320, Height = 820, ShowInTaskbar = false };
        try
        {
            window.Show(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var tabs = (TabControl)view.FindName("GroupDetailsTabs"); tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var picker = (MultiSelectLookupButton)view.FindName("CategoryPicker");
            var popup = (Popup)picker.FindName("PART_Popup");
            CollectionAssert.AreEqual(new[] { fixture.RootId }, fixture.ViewModel.SelectedCategorySelection!.SelectedCategories.Select(row => row.Uid).ToArray());
            popup.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.Loaded);
            var search = (TextBox)picker.FindName("PART_SearchBox"); search.Text = "顶喷 / 圆形";
            await Dispatcher.Yield(DispatcherPriority.Background);
            var list = (ListBox)picker.FindName("PART_ListBox");
            Assert.AreEqual(1, list.Items.Count);
            list.UpdateLayout();
            var row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            var check = Descendants(row).OfType<CheckBox>().Single();
            Assert.AreNotEqual(true, check.IsChecked, "继承得到的子分类不能冒充直接关联。");
            check.SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            popup.IsOpen = false; await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(fixture.ViewModel.HasUnsavedChanges);
            popup.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.Loaded);
            search.Text = "顶喷 / 圆形"; await Dispatcher.Yield(DispatcherPriority.Background);
            list.UpdateLayout(); row = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
            Descendants(row).OfType<CheckBox>().Single().SetCurrentValue(ToggleButton.IsCheckedProperty, true);
            await Dispatcher.Yield(DispatcherPriority.DataBind);
            PopupButton(popup, "确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            Assert.IsFalse(popup.IsOpen);
            var request = fixture.ViewModel.BuildRequest();
            Assert.AreEqual(fixture.ChildId, request.CategoryChanges.CreateItems.Single().Data.CategoryId);
            Assert.AreEqual(1, request.GroupChanges.UpdateItems.Single().Version);
            Assert.AreEqual(fixture.GroupId, request.CategoryChanges.CreateItems.Single().Data.AttributeGroupId);
            Assert.IsTrue(fixture.ViewModel.SaveCommand.CanExecute(null));
            var saveButton = Descendants(view).OfType<Button>().Single(button => Equals(button.Content, "保存全部标签"));
            Assert.IsTrue(saveButton.IsEnabled);
            SavePreview(window, "产品标签关联分类.png");
            tabs.SelectedIndex = 0;
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            SavePreview(window, "产品标签标签值.png");
            tabs.SelectedIndex = 1;
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            popup.IsOpen = true; await Dispatcher.Yield(DispatcherPriority.Loaded);
            PopupButton(popup, "清除").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PopupButton(popup, "确定").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var cleared = fixture.ViewModel.BuildRequest();
            Assert.HasCount(0, cleared.CategoryChanges.CreateItems);
            Assert.AreEqual(fixture.RelationId, cleared.CategoryChanges.DeleteItems.Single().Uid);
            Assert.AreEqual(0, fixture.WriteCount);
        }
        finally { window.Close(); await Dispatcher.Yield(DispatcherPriority.ApplicationIdle); }
    });

    /// <summary>把实际页面渲染成任务工作区预览，供人工检查两栏和分类说明；不读取其他应用屏幕。</summary>
    private static void SavePreview(FrameworkElement view, string fileName)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        Assert.IsNotNull(root);
        var directory = Path.Combine(root.FullName, ".tmp", "tag-shared-frontend"); Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(Path.Combine(directory, fileName)); encoder.Save(output);
    }

    /// <summary>仅测试查找真实控件的可观察按钮及勾选框，不在产品实现缓存视觉容器。</summary>
    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private static Button PopupButton(Popup popup, string content) => Descendants(popup.Child)
        .OfType<Button>().Single(button => Equals(button.Content, content));

    /// <summary>只提供当前测试的定义和候选；任何写入都计数并拒绝，不接通真实网络。</summary>
    private sealed class Fixture : IDisposable
    {
        internal readonly Guid RootId = Guid.NewGuid(), ChildId = Guid.NewGuid(), GroupId = Guid.NewGuid(), RelationId = Guid.NewGuid();
        internal int WriteCount;
        internal readonly ProductAttributeManagerViewModel ViewModel;
        internal Fixture()
        {
            var group = new ProductAttributeGroupDto { Uid = GroupId, Version = 1, GroupCode = "INSTALL", GroupName = "安装方式", SelectionMode = ProductAttributeSelectionMode.Multiple };
            group.Options.Add(new() { Uid = Guid.NewGuid(), Version = 1, AttributeGroupId = GroupId, OptionCode = "WALL", OptionName = "挂墙" });
            var relation = new ProductCategoryAttributeGroupDto { Uid = RelationId, Version = 1, CategoryId = RootId, AttributeGroupId = GroupId };
            var snapshot = new ProductAttributeDefinitionsDto { Groups = [group], CategoryRelations = [relation] };
            var cache = new ReferenceDataCacheServiceStub(productAttributeGroups: [group], productAttributeCategoryRelations: [relation],
                productCategories: [new ProductCategoryDto { Uid = RootId, CategoryName = "顶喷" }, new ProductCategoryDto { Uid = ChildId, ParentId = RootId, CategoryName = "圆形" }]);
            cache.ProductCategories.Single(row => row.Uid == ChildId).SetCategoryPath("顶喷 / 圆形");
            var user = new CurrentUserService(); user.SetCurrentUser(new MeDto { UserId = "tag-sta", Permissions = [PermissionKeys.ProductsCategoryView, PermissionKeys.ProductsCategoryEdit] });
            ViewModel = new(Proxy<IProductAttributeApi>((method, _) =>
            {
                if (method == nameof(IProductAttributeApi.GetDefinitionsAsync)) return Task.FromResult(ServiceResult<ProductAttributeDefinitionsDto>.Ok(snapshot));
                WriteCount++; throw new AssertFailedException("本交互测试不应请求写入。");
            }), Proxy<IApiCallRunner>((_, args) => ((Delegate)args[0]!).DynamicInvoke()), cache,
                Proxy<IMessageService>((_, _) => null), Proxy<IDialogService>((method, _) => method == nameof(IDialogService.ShowAsync)
                    ? Task.FromResult(DialogResult.Close) : Task.FromResult(false)), user);
        }
        public void Dispose() => ViewModel.Dispose();
    }

    private static T Proxy<T>(Func<string, object?[], object?> call) where T : class
    {
        var value = DispatchProxy.Create<T, TestProxy>(); ((TestProxy)(object)value).Call = call; return value;
    }

    /// <summary>转发测试接口调用，不拥有生产依赖。</summary>
    public class TestProxy : DispatchProxy
    {
        public Func<string, object?[], object?> Call { get; set; } = null!;
        /// <summary>只转发夹具允许的接口调用。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!.Name, args ?? []);
    }
}

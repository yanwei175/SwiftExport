using System.Collections;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Services.Caching;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

[TestClass]
[DoNotParallelize]
public sealed class ProductCategoryFilterTreeViewTests
{
    [TestMethod]
    public void InitialTree_ShouldPlaceSpecialNodesBeforeCategoryRoots()
    {
        StaTest.RunAsync(() =>
        {
            var child = Category("子分类", 2);
            var root = Category("根分类", 1, child);
            var control = new ProductCategoryFilterTreeView
            {
                Categories = new ObservableCollection<ProductCategoryCacheNode> { root },
            };

            WithLoadedControl(control, tree =>
            {
                CollectionAssert.AreEqual(
                    new[] { "全部分类", "未分类", "根分类" },
                    tree.Items.Cast<object>().Select(node => GetDisplayName(node)).ToArray());
                Assert.AreEqual("子分类", GetDisplayName(tree.Items[2], childIndex: 0));
                Assert.IsTrue(control.Selection.IsAll);
                Assert.IsFalse(control.Selection.IncludeUncategorized);
                Assert.IsEmpty(control.Selection.CategoryIds);
            });

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ParentToggle_ShouldSelectSubtreeAndPublishOnlyOnce()
    {
        StaTest.RunAsync(() =>
        {
            var child = Category("子分类", 2);
            var root = Category("根分类", 1, child);
            var command = new RecordingCommand();
            var control = new ProductCategoryFilterTreeView
            {
                Categories = new[] { root },
                SelectionChangedCommand = command,
            };

            WithLoadedControl(control, tree =>
            {
                ClickItem(GetRootItem(tree, 2));

                Assert.IsFalse(control.Selection.IsAll);
                CollectionAssert.AreEqual(
                    new[] { root.Uid, child.Uid },
                    control.Selection.CategoryIds.ToArray());
                Assert.AreEqual(1, command.ExecuteCount);
                Assert.AreSame(control.Selection, command.LastParameter);
            });

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void PartialSubtree_ShouldKeepParentCategoryIdAndShowIndeterminate()
    {
        StaTest.RunAsync(() =>
        {
            var child = Category("子分类", 2);
            var root = Category("根分类", 1, child);
            var control = new ProductCategoryFilterTreeView { Categories = new[] { root } };

            WithLoadedControl(control, tree =>
            {
                var rootItem = GetRootItem(tree, 2);
                ClickItem(rootItem);
                rootItem.IsExpanded = true;
                rootItem.UpdateLayout();
                var childItem = (TreeViewItem?)rootItem.ItemContainerGenerator.ContainerFromIndex(0);
                Assert.IsNotNull(childItem);

                ClickItem(childItem);

                CollectionAssert.AreEqual(new[] { root.Uid }, control.Selection.CategoryIds.ToArray());
                var checkBox = FindVisualDescendant<CheckBox>(rootItem);
                Assert.IsNotNull(checkBox);
                Assert.IsNull(checkBox.IsChecked);
            });

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void SpecialNodes_ShouldKeepAllExclusiveAndAllowUncategorizedWithCategories()
    {
        StaTest.RunAsync(() =>
        {
            var child = Category("子分类", 2);
            var root = Category("根分类", 1, child);
            var control = new ProductCategoryFilterTreeView { Categories = new[] { root } };

            WithLoadedControl(control, tree =>
            {
                ClickItem(GetRootItem(tree, 1));
                ClickItem(GetRootItem(tree, 2));

                Assert.IsFalse(control.Selection.IsAll);
                Assert.IsTrue(control.Selection.IncludeUncategorized);
                CollectionAssert.AreEqual(
                    new[] { root.Uid, child.Uid },
                    control.Selection.CategoryIds.ToArray());

                ClickItem(GetRootItem(tree, 0));
                Assert.IsTrue(control.Selection.IsAll);
                Assert.IsFalse(control.Selection.IncludeUncategorized);
                Assert.IsEmpty(control.Selection.CategoryIds);

                ClickItem(GetRootItem(tree, 1));
                ClickItem(GetRootItem(tree, 1));
                Assert.IsTrue(control.Selection.IsAll, "取消最后一个实际条件后应自动恢复全部分类。");
            });

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void ExternalSelectionAndSourceReset_ShouldSynchronizeWithoutExecutingCommand()
    {
        StaTest.RunAsync(() =>
        {
            var first = Category("第一分类", 1);
            var second = Category("第二分类", 2);
            var categories = new ObservableCollection<ProductCategoryCacheNode> { first, second };
            var command = new RecordingCommand();
            var control = new ProductCategoryFilterTreeView
            {
                Categories = categories,
                SelectionChangedCommand = command,
            };

            WithLoadedControl(control, _ =>
            {
                control.Selection = new ProductCategoryFilterSelection(
                    false,
                    true,
                    new[] { second.Uid, Guid.NewGuid() });

                Assert.IsFalse(control.Selection.IsAll);
                Assert.IsTrue(control.Selection.IncludeUncategorized);
                CollectionAssert.AreEqual(new[] { second.Uid }, control.Selection.CategoryIds.ToArray());
                Assert.AreEqual(0, command.ExecuteCount);

                categories.Remove(second);
                Assert.IsFalse(control.Selection.IsAll);
                Assert.IsTrue(control.Selection.IncludeUncategorized);
                Assert.IsEmpty(control.Selection.CategoryIds);

                categories.Clear();
                control.Selection = new ProductCategoryFilterSelection(false, false, new[] { first.Uid });
                Assert.IsTrue(control.Selection.IsAll);
                Assert.AreEqual(0, command.ExecuteCount);
            });

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Selection_ShouldSynchronizeThroughRealTwoWayBinding()
    {
        StaTest.RunAsync(() =>
        {
            var category = Category("分类", 1);
            var source = new SelectionBindingSource();
            var control = new ProductCategoryFilterTreeView { Categories = new[] { category } };
            BindingOperations.SetBinding(
                control,
                ProductCategoryFilterTreeView.SelectionProperty,
                new Binding(nameof(SelectionBindingSource.Selection))
                {
                    Source = source,
                    Mode = BindingMode.TwoWay,
                });

            WithLoadedControl(control, tree =>
            {
                ClickItem(GetRootItem(tree, 2));
                Assert.IsFalse(source.Selection.IsAll);
                CollectionAssert.AreEqual(new[] { category.Uid }, source.Selection.CategoryIds.ToArray());

                source.Selection = ProductCategoryFilterSelection.AllCategories;
                Assert.IsTrue(control.Selection.IsAll);
            });

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void SpaceKey_ShouldToggleFocusedNodeAndUnloadedSourceChangesShouldWaitForReload()
    {
        StaTest.RunAsync(() =>
        {
            var first = Category("第一分类", 1);
            var categories = new ObservableCollection<ProductCategoryCacheNode> { first };
            var control = new ProductCategoryFilterTreeView { Categories = categories };
            var window = Show(control);
            var tree = (TreeView)control.FindName("CategoryTree");
            try
            {
                var categoryItem = GetRootItem(tree, 2);
                categoryItem.IsSelected = true;
                categoryItem.Focus();
                tree.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(tree), 0, Key.Space)
                {
                    RoutedEvent = Keyboard.PreviewKeyDownEvent,
                });
                CollectionAssert.AreEqual(new[] { first.Uid }, control.Selection.CategoryIds.ToArray());
            }
            finally
            {
                window.Content = null;
                window.Close();
                control.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            }

            Assert.IsFalse(control.Selection.IsAll, "卸载控件本身不应改变现有筛选快照。");
            categories.Clear();
            Assert.IsFalse(control.Selection.IsAll, "卸载期间不得继续响应分类源事件。");

            window = Show(control);
            try
            {
                Assert.IsTrue(control.Selection.IsAll, "重新加载时应清理已失效分类并回到全部分类。");
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>创建测试分类缓存节点，并通过真实 Children 结构连接后代。</summary>
    private static ProductCategoryCacheNode Category(
        string name,
        int sortNo,
        params ProductCategoryCacheNode[] children)
    {
        var constructor = typeof(ProductCategoryCacheNode).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            null,
            new[] { typeof(ProductCategoryDto) },
            null) ?? throw new AssertFailedException("未找到分类缓存节点构造函数。");
        var node = (ProductCategoryCacheNode)constructor.Invoke(new object[]
        {
            new ProductCategoryDto
            {
                Uid = Guid.NewGuid(),
                CategoryName = name,
                CategoryCode = name,
                FakeName = name,
                SortNo = sortNo,
            },
        });
        var addChild = typeof(ProductCategoryCacheNode).GetMethod(
            "AddChild",
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new AssertFailedException("未找到分类缓存子节点连接方法。");
        foreach (var child in children)
        {
            addChild.Invoke(node, new object[] { child });
        }

        return node;
    }

    /// <summary>在真实 Window 中加载控件，执行断言后可靠关闭窗口。</summary>
    private static void WithLoadedControl(ProductCategoryFilterTreeView control, Action<TreeView> assertion)
    {
        var window = Show(control);
        try
        {
            assertion((TreeView)control.FindName("CategoryTree"));
        }
        finally
        {
            window.Close();
        }
    }

    private static Window Show(ProductCategoryFilterTreeView control)
    {
        var window = new Window
        {
            Width = 480,
            Height = 520,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = control,
        };
        window.Show();
        control.UpdateLayout();
        return window;
    }

    private static TreeViewItem GetRootItem(TreeView tree, int index)
    {
        tree.UpdateLayout();
        return (TreeViewItem?)tree.ItemContainerGenerator.ContainerFromIndex(index)
            ?? throw new AssertFailedException($"未生成根节点容器：{index}。");
    }

    private static void ClickItem(TreeViewItem item)
    {
        var checkBox = FindVisualDescendant<CheckBox>(item)
            ?? throw new AssertFailedException("未生成分类筛选 CheckBox。");
        checkBox.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
    }

    private static string GetDisplayName(object node, int? childIndex = null)
    {
        if (childIndex.HasValue)
        {
            var children = (IList)(node.GetType().GetProperty("Children")?.GetValue(node)
                ?? throw new AssertFailedException("筛选节点缺少 Children。"));
            node = children[childIndex.Value]!;
        }

        return (string)(node.GetType().GetProperty("DisplayName")?.GetValue(node)
            ?? throw new AssertFailedException("筛选节点缺少 DisplayName。"));
    }

    private static T? FindVisualDescendant<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
            {
                return match;
            }

            var nested = FindVisualDescendant<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private sealed class RecordingCommand : ICommand
    {
        public int ExecuteCount { get; private set; }
        public object? LastParameter { get; private set; }
        public event EventHandler? CanExecuteChanged;
        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter)
        {
            ExecuteCount++;
            LastParameter = parameter;
        }

        public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class SelectionBindingSource : DependencyObject
    {
        public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
            nameof(Selection),
            typeof(ProductCategoryFilterSelection),
            typeof(SelectionBindingSource),
            new PropertyMetadata(ProductCategoryFilterSelection.AllCategories));

        public ProductCategoryFilterSelection Selection
        {
            get => (ProductCategoryFilterSelection)GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }
    }
}

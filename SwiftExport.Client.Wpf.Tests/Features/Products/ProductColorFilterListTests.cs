using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Controls;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExportClient.Features.Products.Components;
using SwiftExportClient.Features.Products.Components.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>验证产品颜色筛选选项映射以及 ListBox 多选、外部回写和缓存刷新行为。</summary>
[TestClass]
[DoNotParallelize]
public sealed class ProductColorFilterListTests
{
    /// <summary>选项应复制完整 DTO，并以独立勾选状态避免污染共享缓存。</summary>
    [TestMethod]
    public void Option_ShouldMapCompleteDtoAndKeepIndependentCheckState()
    {
        var source = Color("R", "红色", "Red");
        source.Version = 7;
        source.CreatedBy = "tester";

        var option = source.Adapt<ProductColorFilterOption>();
        option.IsChecked = true;

        Assert.AreEqual(source.Uid, option.Uid);
        Assert.AreEqual(source.Version, option.Version);
        Assert.AreEqual(source.ColorCodeUnique, option.ColorCodeUnique);
        Assert.AreEqual(source.ColorCode, option.ColorCode);
        Assert.AreEqual(source.ColorNameZh, option.ColorNameZh);
        Assert.AreEqual(source.ColorNameEn, option.ColorNameEn);
        Assert.AreEqual(source.CreatedBy, option.CreatedBy);
        Assert.IsTrue(option.IsChecked);
        Assert.IsNull(source.GetType().GetProperty(nameof(ProductColorFilterOption.IsChecked)));
    }

    /// <summary>显示名称应按中文、英文、编码顺序回退。</summary>
    [TestMethod]
    public void DisplayName_ShouldUseStableFallbackOrder()
    {
        Assert.AreEqual("红色", Color("R", "红色", "Red").Adapt<ProductColorFilterOption>().DisplayName);
        Assert.AreEqual("Blue", Color("B", string.Empty, "Blue").Adapt<ProductColorFilterOption>().DisplayName);
        Assert.AreEqual("G", Color("G", string.Empty, string.Empty).Adapt<ProductColorFilterOption>().DisplayName);
    }

    /// <summary>选择快照应排除空标识、去重并保持首次出现顺序。</summary>
    [TestMethod]
    public void Selection_ShouldNormalizeIds()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var selection = new ProductColorFilterSelection([first, Guid.Empty, second, first]);

        CollectionAssert.AreEqual(new[] { first, second }, selection.ColorIds.ToArray());
        Assert.IsEmpty(ProductColorFilterSelection.AllColors.ColorIds);
    }

    /// <summary>真实 ListBox 多选应通过双向依赖属性发布，并接受外部选择恢复。</summary>
    [TestMethod]
    public void List_ShouldPublishMultipleSelectionAndApplyExternalSelection()
    {
        Run(async () =>
        {
            var first = Color("R", "红色", "Red");
            var second = Color("B", "蓝色", "Blue");
            var source = new SelectionBindingSource();
            var control = new ProductColorFilterList { Colors = new[] { first, second } };
            BindingOperations.SetBinding(control, ProductColorFilterList.SelectionProperty, new Binding(nameof(SelectionBindingSource.Selection))
            {
                Source = source,
                Mode = BindingMode.TwoWay,
            });
            var window = Show(control);
            try
            {
                var list = (ListBox)control.FindName("ColorList");
                Assert.AreEqual(SelectionMode.Multiple, list.SelectionMode);
                list.SelectedItems.Add(list.Items[0]);
                list.SelectedItems.Add(list.Items[1]);
                await PumpAsync();
                CollectionAssert.AreEqual(new[] { first.Uid, second.Uid }, source.Selection.ColorIds.ToArray());

                list.SelectedItems.Remove(list.Items[0]);
                await PumpAsync();
                CollectionAssert.AreEqual(new[] { second.Uid }, source.Selection.ColorIds.ToArray());

                source.Selection = new ProductColorFilterSelection([first.Uid]);
                await PumpAsync();
                CollectionAssert.AreEqual(new[] { first.Uid }, control.Selection.ColorIds.ToArray());
                Assert.IsTrue(control.Options[0].IsChecked);
                Assert.IsFalse(control.Options[1].IsChecked);
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>缓存重建只保留有效 UID，卸载期间不得继续响应集合变化。</summary>
    [TestMethod]
    public void CacheReset_ShouldPreserveValidSelectionAndRespectUnloadBoundary()
    {
        Run(async () =>
        {
            var first = Color("R", "红色", "Red");
            var second = Color("B", "蓝色", "Blue");
            var colors = new ObservableCollection<ProductColorDto> { first, second };
            var control = new ProductColorFilterList
            {
                Colors = colors,
                Selection = new ProductColorFilterSelection([second.Uid]),
            };
            var window = Show(control);
            try
            {
                colors.Remove(first);
                await PumpAsync();
                CollectionAssert.AreEqual(new[] { second.Uid }, control.Selection.ColorIds.ToArray());

                colors.Remove(second);
                await PumpAsync();
                Assert.IsEmpty(control.Selection.ColorIds);
            }
            finally
            {
                window.Content = null;
                window.Close();
                await PumpAsync();
            }

            var optionCount = control.Options.Count;
            colors.Add(Color("G", "绿色", "Green"));
            Assert.AreEqual(optionCount, control.Options.Count, "控件卸载后不应继续响应缓存集合事件。");

            window = Show(control);
            try
            {
                Assert.HasCount(1, control.Options);
                Assert.IsEmpty(control.Selection.ColorIds);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static ProductColorDto Color(string code, string chineseName, string englishName) => new()
    {
        Uid = Guid.NewGuid(),
        ColorCodeUnique = code,
        ColorCode = code,
        ColorNameZh = chineseName,
        ColorNameEn = englishName,
    };

    private static Window Show(ProductColorFilterList control)
    {
        var window = new Window
        {
            Width = 320,
            Height = 360,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = control,
        };
        window.Show();
        control.UpdateLayout();
        return window;
    }

    private static async Task PumpAsync() =>
        await Dispatcher.CurrentDispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

    /// <summary>复用测试进程的 STA Application，并加载控件依赖的真实主题及 ListBox 样式。</summary>
    private static void Run(Func<Task> action) => DatePickerAssistTests.RunWithApplication(async () =>
    {
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ThemesDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new global::Wpf.Ui.Markup.ControlsDictionary());
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/SwiftExport.Client.Wpf;component/Resources/ListBox.xaml"),
        });
        await action();
    });

    private sealed class SelectionBindingSource : DependencyObject
    {
        public static readonly DependencyProperty SelectionProperty = DependencyProperty.Register(
            nameof(Selection),
            typeof(ProductColorFilterSelection),
            typeof(SelectionBindingSource),
            new PropertyMetadata(ProductColorFilterSelection.AllColors));

        public ProductColorFilterSelection Selection
        {
            get => (ProductColorFilterSelection)GetValue(SelectionProperty);
            set => SetValue(SelectionProperty, value);
        }
    }
}

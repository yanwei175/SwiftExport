using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

[TestClass]
public sealed class DropDownCheckBoxTests
{
    [TestMethod]
    public void GetterOnlySelectedItemsBinding_ShouldMutateExistingCollectionWithoutReplacingIt()
    {
        StaTest.RunAsync(async () =>
        {
            var source = new GetterOnlySelectionSource();
            var originalCollection = source.SelectedItems;
            var control = new DropDownCheckBox
            {
                Width = 180,
                ItemsSource = new[] { "待确认", "已确认" }
            };
            BindingOperations.SetBinding(
                control,
                DropDownCheckBox.SelectedItemsProperty,
                new Binding(nameof(GetterOnlySelectionSource.SelectedItems))
                {
                    Source = source
                });
            var window = CreateWindow(control);

            try
            {
                window.Show();
                await PumpDispatcherAsync();

                Assert.AreSame(originalCollection, control.SelectedItems);
                var metadata = (FrameworkPropertyMetadata)DropDownCheckBox.SelectedItemsProperty
                    .GetMetadata(typeof(DropDownCheckBox));
                Assert.IsFalse(metadata.BindsTwoWayByDefault);

                var button = (Button?)control.FindName("DropDownButton");
                var itemsControl = (ItemsControl?)control.FindName("OptionItemsControl");
                Assert.IsNotNull(button);
                Assert.IsNotNull(itemsControl);
                RaiseClick(button);
                await PumpDispatcherAsync();

                var first = FindCheckBox(itemsControl, 0);
                first.IsChecked = true;
                await PumpDispatcherAsync();

                Assert.AreSame(originalCollection, source.SelectedItems);
                CollectionAssert.AreEqual(new[] { "待确认" }, source.SelectedItems.ToArray());

                first.IsChecked = false;
                await PumpDispatcherAsync();
                Assert.IsEmpty(source.SelectedItems);
            }
            finally
            {
                control.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    [TestMethod]
    public void SelectionText_ShouldFollowItemsSourceOrderAndExternalSelection()
    {
        StaTest.RunAsync(async () =>
        {
            var items = new ObservableCollection<string> { "A", "B", "C", "D" };
            var selectedItems = new ObservableCollection<string> { "B", "D" };
            var control = new DropDownCheckBox
            {
                Width = 180,
                Placeholder = "物料类型",
                ItemsSource = items,
                SelectedItems = selectedItems
            };
            var window = CreateWindow(control);

            try
            {
                window.Show();
                await PumpDispatcherAsync();

                Assert.AreEqual("2,4", control.SelectionText);

                selectedItems.Add("A");
                await PumpDispatcherAsync();
                Assert.AreEqual("1,2,4", control.SelectionText);

                selectedItems.Remove("B");
                items.Move(3, 0);
                await PumpDispatcherAsync();
                Assert.AreEqual("1,2", control.SelectionText, "序号必须跟随当前 ItemsSource 顺序重算。");

                selectedItems.Clear();
                await PumpDispatcherAsync();
                Assert.AreEqual("物料类型", control.SelectionText);
            }
            finally
            {
                control.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    [TestMethod]
    public void TriggerButton_ShouldOpenAndCloseRealPopup()
    {
        StaTest.RunAsync(async () =>
        {
            var control = new DropDownCheckBox
            {
                Width = 180,
                ItemsSource = new[] { "成品", "零件" }
            };
            var window = CreateWindow(control);

            try
            {
                window.Show();
                await PumpDispatcherAsync();

                var button = (Button?)control.FindName("DropDownButton");
                var popup = (Popup?)control.FindName("DropDownPopup");
                var popupSurface = (Border?)control.FindName("PopupSurface");
                Assert.IsNotNull(button);
                Assert.IsNotNull(popup);
                Assert.IsNotNull(popupSurface);
                Assert.IsFalse(control.IsDropDownOpen);
                Assert.IsFalse(popup.IsOpen);

                RaiseClick(button);
                await PumpDispatcherAsync();

                Assert.IsTrue(control.IsDropDownOpen, "点击触发按钮后公开状态应为打开。");
                Assert.IsTrue(popup.IsOpen, "点击触发按钮必须真正打开原生 Popup。");
                Assert.IsNotNull(
                    PresentationSource.FromVisual(popupSurface),
                    "Popup 内容必须连接到真实 PresentationSource，不能只修改状态属性。");
                Assert.IsFalse(popup.StaysOpen);

                RaiseClick(button);
                await PumpDispatcherAsync();

                Assert.IsFalse(control.IsDropDownOpen, "再次点击触发按钮应关闭下拉列表。");
                Assert.IsFalse(popup.IsOpen);
            }
            finally
            {
                control.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    [TestMethod]
    public void CheckBoxes_ShouldBeTwoStateAndUpdateOriginalSelectedItems()
    {
        StaTest.RunAsync(async () =>
        {
            var selectedItems = new ObservableCollection<string>();
            var control = new DropDownCheckBox
            {
                Width = 180,
                ItemsSource = new[] { "成品", "零件", "虚拟件", "客供件" },
                SelectedItems = selectedItems
            };
            var window = CreateWindow(control);

            try
            {
                window.Show();
                await PumpDispatcherAsync();

                var button = (Button?)control.FindName("DropDownButton");
                var popup = (Popup?)control.FindName("DropDownPopup");
                var itemsControl = (ItemsControl?)control.FindName("OptionItemsControl");
                Assert.IsNotNull(button);
                Assert.IsNotNull(popup);
                Assert.IsNotNull(itemsControl);

                RaiseClick(button);
                await PumpDispatcherAsync();
                itemsControl.UpdateLayout();

                var popupSurface = (Border?)control.FindName("PopupSurface");
                Assert.IsNotNull(popupSurface);
                Assert.AreEqual(button.ActualWidth, popupSurface.MinWidth, 0.01);
                Assert.AreEqual(control.MaxDropDownHeight, popupSurface.MaxHeight, 0.01);

                var first = FindCheckBox(itemsControl, 0);
                var fourth = FindCheckBox(itemsControl, 3);
                Assert.IsFalse(first.IsThreeState);
                Assert.IsFalse(fourth.IsThreeState);

                first.IsChecked = true;
                fourth.IsChecked = true;
                await PumpDispatcherAsync();

                CollectionAssert.AreEqual(new[] { "成品", "客供件" }, selectedItems.ToArray());
                Assert.AreEqual("1,4", control.SelectionText);
                Assert.IsTrue(popup.IsOpen, "多选过程中 Popup 不应自动关闭。");

                first.IsChecked = false;
                await PumpDispatcherAsync();
                CollectionAssert.AreEqual(new[] { "客供件" }, selectedItems.ToArray());
                Assert.AreEqual("4", control.SelectionText);
            }
            finally
            {
                control.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    [TestMethod]
    public void EscapeAndPopupClose_ShouldSynchronizeOpenState()
    {
        StaTest.RunAsync(async () =>
        {
            var control = new DropDownCheckBox
            {
                Width = 180,
                ItemsSource = new[] { "成品", "零件" }
            };
            var window = CreateWindow(control);

            try
            {
                window.Show();
                await PumpDispatcherAsync();

                var button = (Button?)control.FindName("DropDownButton");
                var popup = (Popup?)control.FindName("DropDownPopup");
                var itemsControl = (ItemsControl?)control.FindName("OptionItemsControl");
                Assert.IsNotNull(button);
                Assert.IsNotNull(popup);
                Assert.IsNotNull(itemsControl);

                RaiseClick(button);
                await PumpDispatcherAsync();
                var first = FindCheckBox(itemsControl, 0);
                RaisePreviewKey(first, Key.Escape);
                await PumpDispatcherAsync();

                Assert.IsFalse(control.IsDropDownOpen, "Esc 应关闭下拉列表并同步公开状态。");
                Assert.IsFalse(popup.IsOpen);
                Assert.IsTrue(button.IsKeyboardFocusWithin, "Esc 关闭后焦点应返回触发按钮。");

                control.IsDropDownOpen = true;
                await PumpDispatcherAsync();
                Assert.IsTrue(popup.IsOpen);
                popup.IsOpen = false;
                await PumpDispatcherAsync();
                Assert.IsFalse(control.IsDropDownOpen, "Popup 因外部点击关闭时必须同步依赖属性。");
            }
            finally
            {
                control.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    [TestMethod]
    public void DisplayMemberPathAndReload_ShouldResynchronizeWithoutStaleSubscriptions()
    {
        StaTest.RunAsync(async () =>
        {
            var items = new ObservableCollection<DisplayItem>
            {
                new("A", "选项 A"),
                new("B", "选项 B")
            };
            var selectedItems = new ObservableCollection<DisplayItem>();
            var control = new DropDownCheckBox
            {
                Width = 180,
                ItemsSource = items,
                SelectedItems = selectedItems,
                DisplayMemberPath = nameof(DisplayItem.Name)
            };
            var window = CreateWindow(control);

            try
            {
                window.Show();
                await PumpDispatcherAsync();

                var itemsControl = (ItemsControl?)control.FindName("OptionItemsControl");
                var button = (Button?)control.FindName("DropDownButton");
                Assert.IsNotNull(itemsControl);
                Assert.IsNotNull(button);
                RaiseClick(button);
                await PumpDispatcherAsync();
                Assert.AreEqual("选项 A", FindCheckBox(itemsControl, 0).Content);

                window.Content = null;
                await PumpDispatcherAsync();
                selectedItems.Add(items[1]);
                Assert.AreEqual("请选择", control.SelectionText, "卸载后不应继续响应旧集合事件。");

                window.Content = control;
                await PumpDispatcherAsync();
                Assert.AreEqual("2", control.SelectionText, "重新加载时应从外部集合恢复选择。");
            }
            finally
            {
                control.IsDropDownOpen = false;
                window.Close();
            }
        });
    }

    private static Window CreateWindow(object content) => new()
    {
        Width = 320,
        Height = 240,
        ShowInTaskbar = false,
        WindowStyle = WindowStyle.None,
        Content = content
    };

    private static CheckBox FindCheckBox(ItemsControl itemsControl, int index)
    {
        var container = itemsControl.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
        if (container is null)
        {
            itemsControl.ApplyTemplate();
            itemsControl.Measure(new Size(240, 400));
            itemsControl.Arrange(new Rect(0, 0, 240, 400));
            itemsControl.UpdateLayout();
            container = itemsControl.ItemContainerGenerator.ContainerFromIndex(index) as DependencyObject;
        }

        Assert.IsNotNull(container, $"找不到第 {index + 1} 个选项容器。");
        var checkBox = FindVisualDescendant<CheckBox>(container);
        Assert.IsNotNull(checkBox, $"找不到第 {index + 1} 个 CheckBox。");
        return checkBox;
    }

    private static void RaiseClick(Button button)
    {
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));
    }

    private static void RaisePreviewKey(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target);
        var args = new KeyEventArgs(
            Keyboard.PrimaryDevice,
            source,
            Environment.TickCount,
            key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent
        };
        target.RaiseEvent(args);
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

    private static async Task PumpDispatcherAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Background);
    }

    private sealed record DisplayItem(string Code, string Name);

    private sealed class GetterOnlySelectionSource
    {
        public ObservableCollection<string> SelectedItems { get; } = [];
    }
}

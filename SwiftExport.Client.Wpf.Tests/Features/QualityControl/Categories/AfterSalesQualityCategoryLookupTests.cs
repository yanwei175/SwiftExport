using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Features.QualityControl.AfterSales.Models;
using SwiftExportClient.Features.QualityControl.Categories.Components;
using SwiftExportClient.Features.QualityControl.Categories.Models;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Features.QualityControl.Categories;

/// <summary>通过真实 Lookup 输入事件验证父级、末级选择和历史停用显示。</summary>
[TestClass]
[DoNotParallelize]
public sealed class AfterSalesQualityCategoryLookupTests
{
    /// <summary>键盘可选父级与末级，双向绑定的售后身份进入写入 DTO；显式清空也回写。</summary>
    [TestMethod]
    public void KeyboardSelectsParentAndChildAndClearWritesHeader()
    {
        StaTest.RunAsync(async () =>
        {
            var parent = Node("外观"); var child = Node("划伤"); parent.Children.Add(child);
            var header = new AfterSalesHeaderEditModel();
            var control = new AfterSalesQualityCategoryLookupTextBox { ItemsSource = new[] { parent } };
            control.SetBinding(AfterSalesQualityCategoryLookupTextBox.SelectedCategoryIdProperty,
                new Binding(nameof(header.QualityCategoryId)) { Source = header, Mode = BindingMode.TwoWay });
            using var host = new Host(control);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var editor = (LookupTextBox)control.FindName("LookupEditor");
            var input = (TextBox)editor.FindName("InputTextBox");
            input.Focus(); Keyboard.Focus(input); KeyPress(input, Key.Down); KeyPress(input, Key.Enter);
            Assert.AreEqual(parent.Uid, header.QualityCategoryId);
            Assert.AreEqual(parent.Uid, header.ToDto().QualityCategoryId);
            KeyPress(input, Key.Down); KeyPress(input, Key.Down); KeyPress(input, Key.Enter);
            Assert.AreEqual(child.Uid, header.QualityCategoryId);
            Assert.AreEqual(child.Uid, header.ToDto().QualityCategoryId);
            ((Button)editor.FindName("BtnClear")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.IsNull(header.QualityCategoryId);
        });
    }

    /// <summary>停用父节点历史值显示但不作为新候选，启用子级仍可选，候选替换不清空原身份。</summary>
    [TestMethod]
    public void DisabledParentRemainsVisibleAndEnabledChildCanBeSelected()
    {
        StaTest.RunAsync(async () =>
        {
            var parent = Node("历史停用分类"); parent.IsEnabled = false;
            var child = Node("有效子类"); parent.Children.Add(child);
            var control = new AfterSalesQualityCategoryLookupTextBox
                { SelectedCategoryId = parent.Uid, ItemsSource = new[] { parent } };
            using var host = new Host(control);
            await Dispatcher.Yield(DispatcherPriority.Loaded);
            var editor = (LookupTextBox)control.FindName("LookupEditor");
            Assert.AreEqual(parent.Uid, control.SelectedCategoryId);
            Assert.AreEqual("历史停用分类", ((TextBlock)editor.FindName("DisplayTextBlock")).Text);
            Assert.IsFalse(control.SelectableItems.Contains(parent));
            Assert.IsTrue(control.SelectableItems.Contains(child));
            control.ItemsSource = Array.Empty<AfterSalesQualityCategoryNode>();
            Assert.AreEqual(parent.Uid, control.SelectedCategoryId);
            control.ItemsSource = new[] { parent };
            var input = (TextBox)editor.FindName("InputTextBox");
            input.Focus(); Keyboard.Focus(input); KeyPress(input, Key.Down); KeyPress(input, Key.Enter);
            Assert.AreEqual(child.Uid, control.SelectedCategoryId);
        });
    }

    private static AfterSalesQualityCategoryNode Node(string name) => new() { Uid = Guid.NewGuid(), CategoryName = name, IsEnabled = true };
    /// <summary>发送路由键事件，使用公共 Lookup 的真实键盘处理路径。</summary>
    private static void KeyPress(UIElement target, Key key) => target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,
        PresentationSource.FromVisual(target)!, Environment.TickCount, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
    /// <summary>为真实控件绑定和输入事件提供 STA 窗口，完成后释放。</summary>
    private sealed class Host : IDisposable
    {
        private readonly Window _window;
        public Host(FrameworkElement content)
        {
            _window = new Window { Content = content, Width = 520, Height = 240, ShowInTaskbar = false, WindowStyle = WindowStyle.None };
            _window.Show(); _window.Activate(); content.UpdateLayout();
        }
        /// <inheritdoc/>
        public void Dispose() => _window.Close();
    }
}

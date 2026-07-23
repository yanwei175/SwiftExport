using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;
using WpfUiTextBox = Wpf.Ui.Controls.TextBox;

namespace SwiftExport.Client.Wpf.Tests.Controls;

[TestClass]
public sealed class PopupTextBoxAssistTests
{
    [TestMethod]
    public void FocusedHost_ShouldOpenOverlayWithLiveTwoWayTextAndDynamicSize()
    {
        StaTest.RunAsync(async () =>
        {
            var host = new TextBox
            {
                Width = 220,
                Height = 30,
                Text = "Host text",
                MaxLength = 40,
            };
            PopupTextBoxAssist.SetIsEnabled(host, true);
            PopupTextBoxAssist.SetPopupHeight(host, 140);
            var window = CreateWindow(host);

            try
            {
                window.Show();
                window.Activate();
                Keyboard.Focus(host);
                await Dispatcher.Yield(DispatcherPriority.Background);

                var (popup, border, editor) = GetSessionParts(host);
                Assert.IsTrue(popup.IsOpen);
                Assert.AreEqual(PlacementMode.Relative, popup.Placement);
                Assert.AreSame(host, popup.PlacementTarget);
                Assert.AreEqual(0d, popup.HorizontalOffset);
                Assert.AreEqual(0d, popup.VerticalOffset);
                Assert.AreEqual(host.ActualWidth, border.Width, 0.1);
                Assert.AreEqual(140d, border.Height);
                Assert.IsTrue(editor.AcceptsReturn);
                Assert.AreEqual(TextWrapping.Wrap, editor.TextWrapping);
                Assert.AreEqual(40, editor.MaxLength);

                var binding = BindingOperations.GetBinding(editor, TextBox.TextProperty);
                Assert.IsNotNull(binding);
                Assert.AreEqual(BindingMode.TwoWay, binding.Mode);
                Assert.AreEqual(UpdateSourceTrigger.PropertyChanged, binding.UpdateSourceTrigger);

                host.Text = "Changed from host";
                Assert.AreEqual("Changed from host", editor.Text);
                editor.Text = "Changed from popup";
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert.AreEqual("Changed from popup", host.Text);

                host.Width = 280;
                window.UpdateLayout();
                await Dispatcher.Yield(DispatcherPriority.Render);
                Assert.AreEqual(host.ActualWidth, border.Width, 0.1);

                PopupTextBoxAssist.SetPopupHeight(host, 190);
                Assert.AreEqual(190d, border.Height);
                Assert.ThrowsExactly<ArgumentException>(
                    () => PopupTextBoxAssist.SetPopupHeight(host, 0));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void EditorLostFocusAndEscape_ShouldCloseWithoutStealingOrReopeningFocus()
    {
        StaTest.RunAsync(async () =>
        {
            var host = new TextBox { Width = 220, Text = "Before" };
            var next = new Button { Content = "Next" };
            PopupTextBoxAssist.SetIsEnabled(host, true);
            var panel = new StackPanel();
            panel.Children.Add(host);
            panel.Children.Add(next);
            var window = CreateWindow(panel);

            try
            {
                window.Show();
                window.Activate();
                Keyboard.Focus(host);
                await Dispatcher.Yield(DispatcherPriority.Background);

                var (popup, _, editor) = GetSessionParts(host);
                Assert.IsTrue(popup.IsOpen);
                editor.Text = "Committed live";

                Keyboard.Focus(next);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsFalse(popup.IsOpen);
                Assert.IsTrue(next.IsKeyboardFocused, "自然失焦关闭不得把焦点抢回宿主。");
                Assert.AreEqual("Committed live", host.Text);

                Keyboard.Focus(host);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsTrue(popup.IsOpen);

                RaisePreviewKey(editor, Key.Escape);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsFalse(popup.IsOpen);
                Assert.IsTrue(host.IsKeyboardFocused);

                Keyboard.Focus(next);
                Keyboard.Focus(host);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsTrue(popup.IsOpen, "下一次真正重新获得焦点时应恢复自动打开。");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    public void WpfUiTextBoxWithExistingText_ShouldOpenAndRespectLifecycle()
    {
        StaTest.RunAsync(async () =>
        {
            var readOnlyHost = new WpfUiTextBox { IsReadOnly = true };
            PopupTextBoxAssist.SetIsEnabled(readOnlyHost, true);
            var readOnlyWindow = CreateWindow(readOnlyHost);
            try
            {
                readOnlyWindow.Show();
                readOnlyWindow.Activate();
                Keyboard.Focus(readOnlyHost);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsNull(GetSession(readOnlyHost));
            }
            finally
            {
                readOnlyWindow.Close();
            }

            var host = new WpfUiTextBox
            {
                Width = 220,
                Height = 32,
                Text = "1001-AH, 1002-BB",
                PlaceholderText = "多行客户货号",
            };
            PopupTextBoxAssist.SetIsEnabled(host, true);
            var next = new Button { Content = "Next" };
            var panel = new StackPanel();
            panel.Children.Add(host);
            panel.Children.Add(next);
            var window = CreateWindow(panel);

            try
            {
                window.Show();
                window.Activate();
                Keyboard.Focus(host);
                await Dispatcher.Yield(DispatcherPriority.Background);

                var (popup, _, editor) = GetSessionParts(host);
                Assert.IsTrue(popup.IsOpen, "WPF-UI TextBox 已有内容时也必须正常展开。");
                Assert.AreEqual(host.Text, editor.Text);

                editor.Text = "1001-AH\n1002-BB";
                await Dispatcher.Yield(DispatcherPriority.DataBind);
                Assert.AreEqual("1001-AH\n1002-BB", host.Text);

                Keyboard.Focus(next);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsFalse(popup.IsOpen, "焦点移动到其他控件后应自然关闭，无需 Esc。");
                Assert.IsTrue(next.IsKeyboardFocused);

                Keyboard.Focus(host);
                await Dispatcher.Yield(DispatcherPriority.Background);
                RaisePreviewKey(editor, Key.Escape);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsFalse(popup.IsOpen);
                Assert.IsTrue(host.IsKeyboardFocused);
                RaisePreviewMouseLeftButtonDown(host);
                await Dispatcher.Yield(DispatcherPriority.Background);
                Assert.IsTrue(
                    popup.IsOpen,
                    "宿主已经拥有焦点时，再次鼠标点击也必须重新展开。");

                PopupTextBoxAssist.SetIsEnabled(host, false);
                Assert.IsNull(GetSession(host));
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateWindow(object content) =>
        new()
        {
            Width = 480,
            Height = 320,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = content,
        };

    private static object? GetSession(TextBox host)
    {
        var field = typeof(PopupTextBoxAssist).GetField(
            "SessionProperty",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(field);
        var property = field.GetValue(null) as DependencyProperty;
        Assert.IsNotNull(property);
        return host.GetValue(property);
    }

    private static (Popup Popup, Border Border, TextBox Editor) GetSessionParts(TextBox host)
    {
        var session = GetSession(host);
        Assert.IsNotNull(session);
        var type = session.GetType();
        return (
            GetField<Popup>(session, type, "_popup"),
            GetField<Border>(session, type, "_border"),
            GetField<TextBox>(session, type, "_editor"));
    }

    private static T GetField<T>(object instance, Type type, string name)
        where T : class
    {
        var field = type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.IsNotNull(field);
        var value = field.GetValue(instance) as T;
        Assert.IsNotNull(value);
        return value;
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

    private static void RaisePreviewMouseLeftButtonDown(UIElement target)
    {
        target.RaiseEvent(new MouseButtonEventArgs(
            Mouse.PrimaryDevice,
            Environment.TickCount,
            MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
        });
    }
}

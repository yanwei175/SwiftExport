using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.TestInfrastructure;
using SwiftExportClient.Resources.Controls;

namespace SwiftExport.Client.Wpf.Tests.Controls;

[TestClass]
public sealed class RightDrawerHostTests
{
    [TestMethod]
    public void OverlayDrawer_ShouldResizeWithinConfiguredBounds()
    {
        StaTest.RunAsync(() =>
        {
            var host = new RightDrawerHost
            {
                Width = 900,
                Height = 500,
                DisplayMode = DrawerDisplayMode.Overlay,
                IsOpen = true,
                IsResizable = true,
                MinDrawerWidth = 360,
                MaxDrawerWidth = 700,
                DrawerWidth = 520
            };
            var window = new Window
            {
                Width = 940,
                Height = 540,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host
            };

            try
            {
                window.Show();
                host.UpdateLayout();
                var thumb = (Thumb?)host.FindName("ResizeThumb");
                Assert.IsNotNull(thumb);

                thumb.RaiseEvent(new DragDeltaEventArgs(-80, 0)
                {
                    RoutedEvent = Thumb.DragDeltaEvent
                });
                Assert.AreEqual(600, host.DrawerWidth);

                host.DrawerWidth = 900;
                Assert.AreEqual(700, host.DrawerWidth);
                host.DrawerWidth = 100;
                Assert.AreEqual(360, host.DrawerWidth);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void DrawerContentTemplate_ShouldUseDrawerContentAsDataContext()
    {
        StaTest.RunAsync(() =>
        {
            var context = new object();
#pragma warning disable CS0618
            var template = new DataTemplate
            {
                VisualTree = new FrameworkElementFactory(typeof(DrawerTestView)),
            };
#pragma warning restore CS0618
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                DrawerContent = context,
                DrawerContentTemplate = template,
            };
            var window = new Window
            {
                Width = 640,
                Height = 440,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = host,
            };
            try
            {
                window.Show();
                host.UpdateLayout();
                var drawerView = FindVisualChild<DrawerTestView>(host);
                Assert.IsNotNull(drawerView);
                Assert.AreSame(context, drawerView.DataContext);
            }
            finally
            {
                window.Close();
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Escape_DefaultDisabledOrMainContentFocused_ShouldNotCloseDrawer()
    {
        StaTest.RunAsync(() =>
        {
            var mainButton = new Button { Content = "Main" };
            var drawerButton = new Button { Content = "Drawer" };
            var closeCommand = new RecordingCommand();
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                MainContent = mainButton,
                DrawerContent = drawerButton,
                CloseCommand = closeCommand,
            };
            using var window = Show(host);

            Focus(window.Window, drawerButton);
            RaiseKeyDown(drawerButton, Key.Escape);
            Assert.IsTrue(host.IsOpen);
            Assert.AreEqual(0, closeCommand.ExecutionCount);

            host.CloseOnEscape = true;
            Focus(window.Window, mainButton);
            RaiseKeyDown(mainButton, Key.Escape);
            Assert.IsTrue(host.IsOpen);
            Assert.AreEqual(0, closeCommand.ExecutionCount);

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Escape_EnabledInsideDrawer_ShouldRespectCloseCommandAndHandledChild()
    {
        StaTest.RunAsync(() =>
        {
            var drawerButton = new Button { Content = "Drawer" };
            var closeCommand = new RecordingCommand();
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                CloseOnEscape = true,
                DrawerContent = drawerButton,
                CloseCommand = closeCommand,
            };
            using var window = Show(host);
            Focus(window.Window, drawerButton);

            var handledByEditor = RaiseKeyDown(drawerButton, Key.Escape, handled: true);
            Assert.IsTrue(handledByEditor.Handled);
            Assert.AreEqual(0, closeCommand.ExecutionCount, "子编辑器已处理的 Esc 不得到达抽屉关闭逻辑。");

            var closeRequest = RaiseKeyDown(drawerButton, Key.Escape);
            Assert.IsTrue(closeRequest.Handled);
            Assert.AreEqual(1, closeCommand.ExecutionCount);
            Assert.IsTrue(host.IsOpen, "关闭命令拥有 IsOpen 的业务决定权。");

            closeCommand.CanExecuteValue = false;
            RaiseKeyDown(drawerButton, Key.Escape);
            Assert.AreEqual(1, closeCommand.ExecutionCount);
            Assert.IsTrue(host.IsOpen, "关闭命令不可执行时不得绕过命令直接关闭。");

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Escape_EnabledWithoutCloseCommand_ShouldCloseDrawer()
    {
        StaTest.RunAsync(() =>
        {
            var drawerButton = new Button { Content = "Drawer" };
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                CloseOnEscape = true,
                DrawerContent = drawerButton,
            };
            using var window = Show(host);
            Focus(window.Window, drawerButton);

            var args = RaiseKeyDown(drawerButton, Key.Escape);
            Assert.IsTrue(args.Handled);
            Assert.IsFalse(host.IsOpen);

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Escape_DataGridEditing_ShouldCancelEditBeforeDrawerClose()
    {
        StaTest.RunAsync(() =>
        {
            var row = new DrawerRow { Name = "Original" };
            var dataGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                ItemsSource = new[] { row },
            };
            dataGrid.Columns.Add(new DataGridTextColumn
            {
                Binding = new Binding(nameof(DrawerRow.Name)),
            });
            var closeCommand = new RecordingCommand();
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                CloseOnEscape = true,
                DrawerContent = dataGrid,
                CloseCommand = closeCommand,
            };
            using var window = Show(host);
            dataGrid.CurrentCell = new DataGridCellInfo(row, dataGrid.Columns[0]);
            dataGrid.ScrollIntoView(row, dataGrid.Columns[0]);
            dataGrid.UpdateLayout();
            Assert.IsTrue(dataGrid.BeginEdit());
            var editor = dataGrid.Columns[0].GetCellContent(row) as TextBox;
            Assert.IsNotNull(editor);
            Focus(window.Window, editor);
            editor.Text = "Changed";

            var preview = RaisePreviewKeyDown(editor, Key.Escape);
            if (!preview.Handled)
            {
                RaiseKeyDown(editor, Key.Escape);
            }

            Assert.IsTrue(host.IsOpen);
            Assert.AreEqual(0, closeCommand.ExecutionCount);
            Assert.AreEqual("Original", row.Name, "第一次 Esc 应由 DataGrid 取消当前 Cell 编辑。");

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    public void Escape_LookupPopup_ShouldClosePopupBeforeDrawerClose()
    {
        StaTest.RunAsync(() =>
        {
            var lookup = new LookupTextBox
            {
                SearchMode = LookupTextBoxSearchMode.Local,
                ItemsSource = new[] { "Alpha", "Beta" },
            };
            var closeCommand = new RecordingCommand();
            var host = new RightDrawerHost
            {
                Width = 600,
                Height = 400,
                IsOpen = true,
                CloseOnEscape = true,
                DrawerContent = lookup,
                CloseCommand = closeCommand,
            };
            using var window = Show(host);
            var input = (TextBox?)lookup.FindName("InputTextBox");
            Assert.IsNotNull(input);
            Focus(window.Window, input);
            lookup.IsDropDownOpen = true;

            var preview = RaisePreviewKeyDown(input, Key.Escape);

            Assert.IsTrue(preview.Handled);
            Assert.IsFalse(lookup.IsDropDownOpen);
            Assert.IsTrue(host.IsOpen);
            Assert.AreEqual(0, closeCommand.ExecutionCount);

            return Task.CompletedTask;
        });
    }

    private static T? FindVisualChild<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant is not null)
            {
                return descendant;
            }
        }

        return null;
    }

    private static WindowScope Show(RightDrawerHost host)
    {
        var window = new Window
        {
            Width = 640,
            Height = 440,
            ShowInTaskbar = false,
            WindowStyle = WindowStyle.None,
            Content = host,
        };
        window.Show();
        host.UpdateLayout();
        return new WindowScope(window);
    }

    private static void Focus(Window window, UIElement element)
    {
        window.Activate();
        element.Focus();
        Keyboard.Focus(element);
        Assert.IsTrue(element.IsKeyboardFocusWithin, "测试控件必须获得真实键盘焦点。");
    }

    private static KeyEventArgs RaiseKeyDown(UIElement target, Key key, bool handled = false)
    {
        var source = PresentationSource.FromVisual(target)
                     ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
            Handled = handled,
        };
        target.RaiseEvent(args);
        return args;
    }

    private static KeyEventArgs RaisePreviewKeyDown(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)
                     ?? throw new InvalidOperationException("测试控件尚未连接到 PresentationSource。");
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, source, Environment.TickCount, key)
        {
            RoutedEvent = Keyboard.PreviewKeyDownEvent,
        };
        target.RaiseEvent(args);
        return args;
    }

    private sealed class WindowScope : IDisposable
    {
        public WindowScope(Window window)
        {
            Window = window;
        }

        public Window Window { get; }

        public void Dispose() => Window.Close();
    }

    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecuteValue { get; set; } = true;
        public int ExecutionCount { get; private set; }

        public bool CanExecute(object? parameter) => CanExecuteValue;

        public void Execute(object? parameter) => ExecutionCount++;
    }

    private sealed class DrawerTestView : UserControl
    {
    }

    private sealed class DrawerRow
    {
        public string Name { get; set; } = string.Empty;
    }
}

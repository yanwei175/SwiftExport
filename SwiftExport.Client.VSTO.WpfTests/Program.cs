using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Hosting;
using SwiftExport.Client.VSTO.UI.Themes;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>独立进程 STA 验证真实 WPF 资源，不加载 Office，不污染认证测试的 Application 单例。</summary>
    internal static class Program
    {
        private static int checks;

        /// <summary>返回零表示所有断言通过；进程退出回收测试 Application，不停止宿主 Dispatcher。</summary>
        [STAThread]
        private static int Main()
        {
            try
            {
                var host = new WpfRuntimeHost();
                host.Initialize();
                var app = Application.Current;
                Check(app.ShutdownMode == ShutdownMode.OnExplicitShutdown, "资源 Application 使用显式关闭模式");
                var count = app.Resources.MergedDictionaries.Count;
                host.Initialize();
                Check(app.Resources.MergedDictionaries.Count == count, "重复初始化不重复合并");
                CheckAccent();
                CheckButton();
                host.Dispose();
                host.Dispose();
                Check(app.Resources.MergedDictionaries.Count == count - 1, "幂等卸载移除自有字典");
                Check(Application.Current == app && !app.Dispatcher.HasShutdownStarted, "卸载不停止 Application 或 Dispatcher");
                Check(!app.Resources.Contains("AccentFillColorDefault"), "卸载移除新增的根强调色");
                ThrowsDisposed(() => host.Initialize());

                var original = new SolidColorBrush(Colors.Orange);
                app.Resources["SystemAccentBrush"] = original;
                var unrelated = new ResourceDictionary();
                app.Resources.MergedDictionaries.Add(unrelated);
                using (var borrowed = new WpfRuntimeHost())
                {
                    borrowed.Initialize();
                    CheckAccent();
                }
                Check(ReferenceEquals(original, app.Resources["SystemAccentBrush"]), "借用 Application 恢复原有根资源");
                Check(app.Resources.MergedDictionaries.Contains(unrelated), "保留其他使用者的字典");

                var dictionary = Load("Resources/Themes.xaml");
                app.Resources.MergedDictionaries.Add(dictionary);
                var theme = new ThemeService(dictionary, app.Dispatcher);
                foreach (var name in new[] { "HC1", "HC2", "HCBlack", "HCWhite" })
                {
                    theme.ApplyTheme(name, true);
                    Check(dictionary.MergedDictionaries[0].Source.ToString().EndsWith(name + ".xaml"), name + "主题加载");
                    Check(ReferenceEquals(dictionary["AccentButtonBackground"], SystemColors.HighlightBrush), "高对比度遵循系统选择背景");
                    Check(ReferenceEquals(dictionary["AccentButtonForeground"], SystemColors.HighlightTextBrush), "高对比度遵循系统选择文字");
                    CheckButton();
                }
                theme.ApplyTheme("Light", false);
                CheckAccent();
                CheckButton();
                var newer = new SolidColorBrush(Colors.Purple);
                app.Resources["SystemAccentBrush"] = newer;
                theme.QueueRefresh();
                theme.Dispose();
                var frame = new DispatcherFrame();
                app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Check(ReferenceEquals(newer, app.Resources["SystemAccentBrush"]), "卸载不覆盖后来者的根资源写入");
                Check(!app.Resources.Contains("AccentFillColorDefault"), "释放后排队刷新不重新写入资源");
                ThrowsDisposed(() => theme.ApplyTheme("Light", false));
                app.Resources.MergedDictionaries.Remove(dictionary);

                var uiResources = Wpf.Ui.UiApplication.Current.Resources;
                var failed = false;
                try { new ThemeService(new ResourceDictionary(), app.Dispatcher); }
                catch (ArgumentOutOfRangeException) { failed = true; }
                Check(failed && !app.Resources.Contains("AccentFillColorDefault"), "初始化失败恢复根资源");
                Check(ReferenceEquals(uiResources, Wpf.Ui.UiApplication.Current.Resources), "初始化失败恢复 WPF-UI 入口");

                var preview = Load("Properties/DesignTimeResources.xaml");
                app.Resources.MergedDictionaries.Add(preview);
                CheckButton();
                app.Resources.MergedDictionaries.Remove(preview);
                Console.WriteLine("PASS: " + checks + " assertions");
                return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }

        // 检查实际 Color，避免仅存在同名 Brush 却遗漏按钮所需颜色。
        private static void CheckAccent()
        {
            foreach (var key in new[] { "AccentFillColorDefault", "AccentFillColorSecondary", "AccentFillColorTertiary" })
                Check(((Color)Application.Current.FindResource(key)).A > 0, key + " 非透明");
        }

        // 加载真实控件模板；检查状态画刷及禁用触发器，不用源码文本断言替代 WPF 解析。
        private static void CheckButton()
        {
            var button = new Wpf.Ui.Controls.Button { Appearance = Wpf.Ui.Controls.ControlAppearance.Primary, Content = "登录" };
            var window = new Window { Content = button };
            window.Measure(new Size(400, 200));
            window.Arrange(new Rect(0, 0, 400, 200));
            button.ApplyTemplate();
            Check(Visible(button.Background), "Primary 普通背景");
            Check(Visible(button.MouseOverBackground), "Primary 悬停背景");
            Check(Visible(button.PressedBackground), "Primary 按下背景");
            Check(Visible(button.Foreground), "Primary 文字");
            Check(button.FocusVisualStyle != null, "键盘焦点样式存在");
            button.IsEnabled = false;
            var border = (Border)button.Template.FindName("ContentBorder", button);
            Check(Visible(border.Background) && Visible(button.Foreground), "禁用背景及文字");
            button.IsEnabled = true;
            Check(Visible(button.Background), "恢复启用背景");
            window.Content = null;
            window.Close();
        }

        private static bool Visible(Brush brush) { return brush is SolidColorBrush solid && solid.Color.A > 0 && solid.Opacity > 0; }
        private static ResourceDictionary Load(string path)
        {
            return new ResourceDictionary { Source = new Uri("/" + typeof(Program).Assembly.GetName().Name + ";component/" + path, UriKind.Relative) };
        }
        private static void ThrowsDisposed(Action action)
        {
            try { action(); }
            catch (ObjectDisposedException) { Check(true, "释放后禁止更新"); return; }
            throw new InvalidOperationException("释放后仍允许更新");
        }
        private static void Check(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException(description);
            checks++;
        }
    }
}

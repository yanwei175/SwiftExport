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
        private static int Main(string[] args)
        {
            // 测试直接调用页面命令时也使用 UI 同步上下文，模拟宿主消息循环中的按钮回调。
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            try
            {
                if (args.Length == 1 && args[0] == "--typography-only")
                {
                    using (var fontHost = new WpfRuntimeHost()) { fontHost.Initialize(); TypographyChecks.Run(Check); }
                    Console.WriteLine("PASS Typography: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--purchase-list-only")
                {
                    using (var listHost = new WpfRuntimeHost()) { listHost.Initialize(); PurchaseOrderListChecks.Run(Check); }
                    Console.WriteLine("PASS Purchase order list: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--sales-list-only")
                {
                    using (var listHost = new WpfRuntimeHost()) { listHost.Initialize(); SalesOrderListChecks.Run(Check); }
                    Console.WriteLine("PASS Sales order list: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--packing-import-only")
                {
                    using (var packingHost = new WpfRuntimeHost()) { packingHost.Initialize(); PackingListImportChecks.Run(Check); }
                    Console.WriteLine("PASS PackingList import: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--purchase-preview-only")
                {
                    using (var previewHost = new WpfRuntimeHost())
                    {
                        previewHost.Initialize();
                        PurchasePreviewChecks.Run(Check);
                    }
                    Console.WriteLine("PASS Purchase preview: " + checks + " assertions");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--read-profile-only")
                {
                    using (var profileHost = new WpfRuntimeHost()) { profileHost.Initialize(); ReadProfilePageChecks.Run(Check); }
                    Console.WriteLine("PASS Read profile: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--modeless-host-only")
                {
                    using (var settingsHost = new WpfRuntimeHost())
                    {
                        settingsHost.Initialize();
                        ModelessHostChecks.Run(Check);
                    }
                    Console.WriteLine("PASS Modeless host: " + checks + " assertions");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--design-resources-only")
                {
                    var designApp = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    designApp.Resources.MergedDictionaries.Add(Load("Properties/DesignTimeResources.xaml"));
                    ImplicitStyleChecks.Run(Check);
                    Console.WriteLine("PASS Design resources: " + checks + " assertions");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--implicit-only")
                {
                    ImplicitStyleChecks.RunLifecycle(Check);
                    Console.WriteLine("PASS Implicit: " + checks + " assertions");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--sales-submission-only")
                {
                    using (var submissionHost = new WpfRuntimeHost()) { submissionHost.Initialize(); SubmissionPageChecks.Run(Check); }
                    Console.WriteLine("PASS Sales submission: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--sales-snapshot-only")
                {
                    using (var snapshotHost = new WpfRuntimeHost()) { snapshotHost.Initialize(); SubmissionPageChecks.CheckSnapshotControls(Check); }
                    Console.WriteLine("PASS Sales snapshot: " + checks + " assertions"); return 0;
                }
                if (args.Length == 1 && args[0] == "--import-only")
                {
                    using (var importHost = new WpfRuntimeHost())
                    {
                        importHost.Initialize();
                        ImportPageChecks.Run(Check);
                        SubmissionPageChecks.Run(Check);
                    }
                    Console.WriteLine("PASS Import: " + checks + " assertions");
                    return 0;
                }
                if (args.Length == 1 && args[0] == "--lookup-only")
                {
                    RunLookupOnly();
                    Console.WriteLine("PASS Lookup: " + checks + " assertions");
                    return 0;
                }
                var host = new WpfRuntimeHost();
                host.Initialize();
                var app = Application.Current;
                ImplicitStyleChecks.Run(Check);
                Check(app.ShutdownMode == ShutdownMode.OnExplicitShutdown, "资源 Application 使用显式关闭模式");
                var count = app.Resources.MergedDictionaries.Count;
                host.Initialize();
                Check(app.Resources.MergedDictionaries.Count == count, "重复初始化不重复合并");
                CheckAccent();
                CheckButton();
                CheckStyles();
                CheckConverters();
                ImportPageChecks.Run(Check);
                        SubmissionPageChecks.Run(Check);
                LookupTextBoxChecks.Run(Check);
                BusinessLookupChecks.Run(Check);
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
                Check(dictionary.MergedDictionaries[0].Source.ToString().EndsWith("Light.xaml"), "固定 Light 主题加载");
                CheckAccent();
                CheckButton();
                var newer = new SolidColorBrush(Colors.Purple);
                app.Resources["SystemAccentBrush"] = newer;
                theme.Dispose();
                var frame = new DispatcherFrame();
                app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Check(ReferenceEquals(newer, app.Resources["SystemAccentBrush"]), "卸载不覆盖后来者的根资源写入");
                Check(!app.Resources.Contains("AccentFillColorDefault"), "释放后移除自有强调色资源");
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

        /// <summary>单独验证 Lookup，便于其他样式有在途改动时明确报告专项结果；默认仍运行完整断言。</summary>
        private static void RunLookupOnly()
        {
            using (var host = new WpfRuntimeHost())
            {
                host.Initialize();
                LookupTextBoxChecks.Run(Check);
                BusinessLookupChecks.Run(Check);
            }
            var dictionary = Load("Resources/Themes.xaml");
            Application.Current.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                using (var theme = new ThemeService(dictionary, Application.Current.Dispatcher))
                {
                    LookupTextBoxChecks.CheckAppearance(Check);
                }
            }
            finally { Application.Current.Resources.MergedDictionaries.Remove(dictionary); }
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

        // 逐一解析移植资源，实例化实际控件；避免仅编译通过但延迟资源引用在开页时失败。
        private static void CheckStyles()
        {
            var root = Application.Current.Resources.MergedDictionaries[Application.Current.Resources.MergedDictionaries.Count - 1];
            foreach (var dictionary in root.MergedDictionaries)
            {
                if (dictionary.Source == null || dictionary.Source.ToString().IndexOf("Wpf.Ui", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                foreach (var key in dictionary.Keys)
                {
                    var value = dictionary[key];
                    Check(value != null, "资源解析：" + key);
                    var style = value as Style;
                    if (style == null || style.TargetType.IsAbstract) continue;
                    var element = Activator.CreateInstance(style.TargetType) as FrameworkElement;
                    if (element == null) continue;
                    element.Style = style;
                    if (element is ContextMenu)
                    {
                        element.ApplyTemplate();
                        Check(style.IsSealed, "独立菜单样式实例化");
                        continue;
                    }
                    var window = new Window { Content = element };
                    window.Measure(new Size(640, 480));
                    window.Arrange(new Rect(0, 0, 640, 480));
                    element.ApplyTemplate();
                    Check(element.Style.IsSealed, "样式实例化：" + key);
                    window.Content = null;
                    window.Close();
                }
            }
            var grid = new DataGrid { Style = (Style)Application.Current.FindResource("SwiftExportDataGrid") };
            grid.Columns.Add(new DataGridTextColumn
            {
                Binding = new System.Windows.Data.Binding("Name"),
                ElementStyle = (Style)Application.Current.FindResource("SwiftExportDataGridTextBlock"),
                EditingElementStyle = (Style)Application.Current.FindResource("SwiftExportDataGridTextBox")
            });
            grid.ItemsSource = new[] { new { Name = "订单预览" }, new { Name = "API 匹配结果" } };
            var host = new Window { Content = grid };
            grid.ApplyTemplate();
            grid.Measure(new Size(640, 480));
            grid.Arrange(new Rect(0, 0, 640, 480));
            grid.UpdateLayout();
            var row = (DataGridRow)grid.ItemContainerGenerator.ContainerFromIndex(0);
            Check(row != null, "原生 DataGrid 行已生成");
            grid.SelectedItem = grid.Items[0];
            Check(row.IsSelected, "原生选中行可用");
            Check(Equals(((SolidColorBrush)row.Foreground).Color,
                ((SolidColorBrush)Application.Current.FindResource("SwiftExportBrush.DataGridSelectedForeground")).Color), "选中行文字使用主题配对颜色");
            Check(DependencyPropertyHelper.GetValueSource(grid, DataGrid.SelectionUnitProperty).BaseValueSource == BaseValueSource.Default,
                "样式不接管选择模式");
            host.Content = null;
            host.Close();
        }

        // 验证样式所需转换器的未就绪与正常输入，不加载导入业务或网络。
        private static void CheckConverters()
        {
            var padding = new Resources.Converters.DatePickerButtonPaddingConverter();
            var culture = System.Globalization.CultureInfo.InvariantCulture;
            Check(Equals(new Thickness(1, 2, 32, 4), padding.Convert(new object[] { new Thickness(1, 2, 3, 4), new Thickness(2, 0, 3, 0), 24d }, typeof(Thickness), null, culture)), "日期按钮预留宽度");
            Check(ReferenceEquals(DependencyProperty.UnsetValue, padding.Convert(new object[] { DependencyProperty.UnsetValue }, typeof(Thickness), null, culture)), "未就绪绑定不产生错误布局");
            var error = new Resources.Converters.FirstValidationErrorConverter();
            Check(error.Convert(new object[0], typeof(string), null, culture) == null, "验证错误清空时无提示");
            var validation = new ValidationError(new ExceptionValidationRule(), new object(), "匹配失败", null);
            Check(Equals("匹配失败", error.Convert(new[] { validation }, typeof(string), null, culture)), "显示首条原生验证提示");
        }
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

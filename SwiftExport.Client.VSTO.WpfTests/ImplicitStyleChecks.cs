using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>验证从宿主加载资源后不显式赋值 Style 的真实隐式样式查找。</summary>
    internal static class ImplicitStyleChecks
    {
        internal static void Run(Action<bool, string> check)
        {
            var date = new DatePicker();
            var text = new TextBox();
            var uiText = new Wpf.Ui.Controls.TextBox();
            var panel = new StackPanel(); panel.Children.Add(date); panel.Children.Add(text); panel.Children.Add(uiText);
            var window = new Window { Style = null, Content = panel, Width = 450, Height = 300, ShowInTaskbar = false };
            try
            {
                window.Show();
                var frame = new DispatcherFrame();
                window.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                foreach (var control in new Control[] { date, text, uiText })
                {
                    var style = control.TryFindResource(control.GetType()) as Style;
                    Console.WriteLine(control.GetType().FullName + ": resource=" + (style != null) + "; style=" + (control.Style != null)
                        + "; source=" + DependencyPropertyHelper.GetValueSource(control, FrameworkElement.StyleProperty).BaseValueSource);
                    check(style != null, control.GetType().Name + " 类型资源存在");
                    check(ReferenceEquals(style, control.Style), control.GetType().Name + " 自动应用类型隐式样式");
                    check(control.Template != null, control.GetType().Name + " 已解析控件模板");
                }
                check(ReferenceEquals(date.Style.BasedOn, Application.Current.TryFindResource("CompactDatePickerStyle")), "日期控件继承项目紧凑模板");
                check(text.Template.FindName("AccentBorder", text) != null, "原生 TextBox 使用 WPF-UI AccentBorder 模板部件");
                check(uiText.Template.FindName("PlaceholderTextBox", uiText) != null, "ui:TextBox 使用 WPF-UI 占位模板部件");
                var dateText = (System.Windows.Controls.Primitives.DatePickerTextBox)date.Template.FindName("PART_TextBox", date);
                dateText.ApplyTemplate();
                check(dateText.Template.FindName("AccentBorder", dateText) != null, "日期内部文本框同样采用 WPF-UI 模板");
                check(date.Style.BasedOn.BasedOn != null && date.Background != null && date.BorderBrush != null, "紧凑日期保留官方基础外观属性");
                text.IsReadOnly = true; uiText.IsReadOnly = true;
                check(text.BorderThickness == new Thickness(0) && uiText.BorderThickness == new Thickness(0), "合并顺序保留项目只读无边框覆盖");
                var explicitDate = (DatePicker)XamlReader.Parse("<DatePicker xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" Style=\"{StaticResource {x:Type DatePicker}}\"/>");
                check(ReferenceEquals(explicitDate.Style, date.Style), "x:Type DatePicker 静态引用可以解析");
            }
            finally { window.Close(); }
        }
        /// <summary>验证官方主题初始化、固定 Light、幂等启动和借用资源释放。</summary>
        internal static void RunLifecycle(Action<bool, string> check)
        {
            var themeEvents = 0;
            Wpf.Ui.Appearance.ThemeChangedEvent changed = (theme, accent) => themeEvents++;
            Wpf.Ui.Appearance.ApplicationThemeManager.Changed += changed;
            var host = new Hosting.WpfRuntimeHost();
            try
            {
                host.Initialize();
                check(Application.Current is App, "宿主显式创建 App 并加载 App.xaml");
                check(themeEvents == 1, "启动通过官方 Apply 初始化一次主题");
                check(Wpf.Ui.Appearance.ApplicationThemeManager.GetAppTheme() == Wpf.Ui.Appearance.ApplicationTheme.Light, "官方主题状态为 Light");
                var app = Application.Current;
                var root = app.Resources.MergedDictionaries[0];
                check(root.MergedDictionaries[0].Source.ToString().EndsWith("Light.xaml"), "唯一主题字典为 Light");
                host.Initialize(); check(themeEvents == 1 && app.Resources.MergedDictionaries.Count == 1, "重复初始化不重复主题和字典");
                Run(check);
                host.Dispose(); host.Dispose();
                check(app.Resources.MergedDictionaries.Count == 0 && !app.Dispatcher.HasShutdownStarted, "卸载资源不关闭宿主 Dispatcher");
                var unrelated = new ResourceDictionary { Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Dark.xaml") };
                app.Resources.MergedDictionaries.Add(unrelated);
                using (var borrowed = new Hosting.WpfRuntimeHost())
                {
                    borrowed.Initialize();
                    check(ReferenceEquals(app.Resources.MergedDictionaries[0], unrelated), "借用 Application 不替换其他插件的主题");
                    check(app.Resources.MergedDictionaries[1].MergedDictionaries[0].Source.ToString().EndsWith("Light.xaml"), "借用 Application 时插件仍固定 Light");
                }
                check(app.Resources.MergedDictionaries.Count == 1 && ReferenceEquals(app.Resources.MergedDictionaries[0], unrelated), "卸载仅移除插件字典");
                app.Resources.MergedDictionaries.Remove(unrelated);
            }
            finally { host.Dispose(); Wpf.Ui.Appearance.ApplicationThemeManager.Changed -= changed; }
        }
    }
}

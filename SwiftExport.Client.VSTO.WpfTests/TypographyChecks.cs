using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using SwiftExport.Client.VSTO.Hosting.TaskPanes;
using SwiftExport.Client.VSTO.Hosting.Windows;

namespace SwiftExport.Client.VSTO.WpfTests
{
    /// <summary>用真实资源和 ElementHost 验证统一字体、宿主隔离及显式字体例外。</summary>
    internal static class TypographyChecks
    {
        /// <summary>在独立 STA 中验证继承，不依赖 Office 或远程 API。</summary>
        internal static void Run(Action<bool, string> check)
        {
            var font = (FontFamily)Application.Current.FindResource("SwiftExportFont");
            check(font.Source == "Microsoft YaHei, 微软雅黑, Microsoft YaHei UI, 微软雅黑 UI, Segoe UI, Arial", "微软雅黑中英文名称优先，后续字体用于回退");
            check(font.Equals(Application.Current.FindResource("SwiftExportFontFamily"))
                && font.Equals(Application.Current.FindResource("UserFontOrder")), "旧字体键使用相同回退顺序");
            var standaloneText = new TextBlock { Text = "中文 ABC" };
            var styledText = new TextBlock { Style = (Style)Application.Current.FindResource("TitleTextBlock") };
            var textPanel = new StackPanel(); textPanel.Children.Add(standaloneText); textPanel.Children.Add(styledText);
            var textWindow = new Window { Content = textPanel, Width = 300, Height = 150 };
            try
            {
                textWindow.Show(); Pump();
                check(standaloneText.FontFamily.Equals(font), "普通 TextBlock 自动应用字体");
                check(styledText.FontFamily.Equals(font) && styledText.FontSize == 28, "命名文字样式统一字体并保留字号");
            }
            finally { textWindow.Close(); }

            var page = CreatePage();
            using (var pane = new TaskPaneHostControl(page))
            {
                pane.CreateControl(); Pump();
                CheckPage(page, font, check, "任务窗格");
                using (var otherFont = new System.Drawing.Font("Courier New", 11))
                {
                    pane.Font = otherFont; Pump();
                    CheckPage(page, font, check, "任务窗格宿主字体改变后");
                }
            }
            using (var form = new ModelessElementHostForm("字体测试", new System.Drawing.Size(800, 600), IntPtr.Zero))
            {
                page = CreatePage(); form.LoadWpfUI(page); form.Show(); Pump();
                CheckPage(page, font, check, "非模态 WinForms");
                using (var otherFont = new System.Drawing.Font("Courier New", 11))
                {
                    form.Font = otherFont; Pump();
                    CheckPage(page, font, check, "非模态宿主字体改变后");
                }
                form.Close();
            }
            using (var manager = new WindowManager())
            {
                var window = new Window { Content = new TextBox(), Width = 250, Height = 150 };
                manager.Show("font-test", IntPtr.Zero, () => window, null); Pump();
                check(window.FontFamily.Equals(font) && ((TextBox)window.Content).FontFamily.Equals(font), "独立 WPF 窗口统一字体");
                window.Close();
            }
        }

        // 用派生 UserControl 验证真实页面场景，不能只验证基类隐式样式。
        private static FontTestPage CreatePage()
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock { Text = "中文 ABC" });
            panel.Children.Add(new TextBox { Text = "输入" });
            panel.Children.Add(new Wpf.Ui.Controls.TextBox { Text = "输入" });
            panel.Children.Add(new ComboBox());
            panel.Children.Add(new DataGrid());
            panel.Children.Add(new TextBlock { Text = "显式字体例外", FontFamily = new FontFamily("Consolas") });
            panel.Children.Add(new Wpf.Ui.Controls.FontIcon { FontFamily = new FontFamily("Segoe MDL2 Assets"), Glyph = "\uE10F" });
            panel.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Add24 });
            return new FontTestPage { Content = panel, FontSize = 17, FontWeight = FontWeights.SemiBold };
        }

        // 检查正文控件，不把根属性正确误当作所有子控件都正确。
        private static void CheckPage(FontTestPage page, FontFamily font, Action<bool, string> check, string context)
        {
            check(page.FontFamily.Equals(font) && page.FontSize == 17 && page.FontWeight == FontWeights.SemiBold, context + "保留字号字重");
            var panel = (StackPanel)page.Content;
            for (var index = 0; index < 5; index++)
            {
                var element = panel.Children[index];
                check(font.Equals(element.GetValue(System.Windows.Documents.TextElement.FontFamilyProperty)), context + "子控件 " + index + "字体正确");
            }
            check(((TextBlock)panel.Children[5]).FontFamily.Source == "Consolas", context + "保留显式特殊字体");
            var fontIcon = (Wpf.Ui.Controls.FontIcon)panel.Children[6];
            check(fontIcon.FontFamily.Source == "Segoe MDL2 Assets", context + "保留图标字体");
            check(!((Wpf.Ui.Controls.SymbolIcon)panel.Children[7]).FontFamily.Equals(font), context + "SymbolIcon 不使用正文字体");
        }

        // 驱动样式、资源及 WinForms 到 WPF 的映射消息。
        private static void Pump()
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }

        /// <summary>模拟实际业务页面的派生控件。</summary>
        private sealed class FontTestPage : UserControl { }
    }
}

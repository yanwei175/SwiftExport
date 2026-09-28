using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace SwiftExport.Client.VSTO.UI.Themes
{
    /// <summary>维护插件浅色、高对比度和强调色；只替换插件字典，不改变 Office 主题。</summary>
    public sealed class ThemeService : IDisposable
    {
        private readonly ResourceDictionary resources;
        private readonly ResourceDictionary applicationResources;
        private readonly Dispatcher dispatcher;
        private readonly ResourceDictionary previousUiResources;
        private readonly bool previousAutoAccent;
        private readonly Dictionary<object, object> originals = new Dictionary<object, object>();
        private readonly Dictionary<object, object> written = new Dictionary<object, object>();
        private static readonly object Missing = new object();
        private volatile bool disposed;

        /// <summary>在 Application 所属 STA 初始化主题；失败时恢复官方 API 写入的资源。</summary>
        public ThemeService(ResourceDictionary resources, Dispatcher dispatcher)
        {
            this.resources = resources;
            this.dispatcher = dispatcher;
            dispatcher.VerifyAccess();
            applicationResources = Application.Current.Resources;
            // 4.3.0 仅按顶层 Wpf.Ui 字典识别 Application；本项目使用公共包装字典，必须显式接线。
            var ui = Wpf.Ui.UiApplication.Current;
            previousAutoAccent = ui.ApplySystemAccentColor;
            ui.ApplySystemAccentColor = false;
            previousUiResources = ui.Resources;
            ui.Resources = applicationResources;
            try { Apply(); }
            catch { RestoreAccentResources(); RestoreUiResources(); throw; }
            SystemParameters.StaticPropertyChanged += SystemChanged;
        }

        // 系统事件可能来自后台线程，排队回调必须再次检查释放状态。
        private void SystemChanged(object sender, PropertyChangedEventArgs args)
        {
            QueueRefresh();
        }

        /// <summary>跨线程排队刷新系统主题，释放后的通知被丢弃。</summary>
        internal void QueueRefresh()
        {
            if (disposed || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (disposed) return;
                try { Apply(); }
                catch (Exception ex) { new Diagnostics.AddInLog().Write("ThemeUpdateFailure", ex); }
            }));
        }

        // 只更新本插件的第一层主题，不调用会搜索并替换其他应用字典的全局主题 API。
        private void Apply()
        {
            SystemThemeManager.UpdateSystemThemeCache();
            var contrast = SystemParameters.HighContrast;
            var name = "Light";
            if (contrast)
            {
                var system = SystemThemeManager.GetCachedSystemTheme().ToString();
                name = system == "HC1" || system == "HC2" || system == "HCBlack" || system == "HCWhite"
                    ? system : "HCWhite";
            }
            ApplyTheme(name, contrast);
        }

        /// <summary>在 STA 应用已解析的主题；内部入口供独立 WPF 验证模拟高对比度切换。</summary>
        internal void ApplyTheme(string name, bool contrast)
        {
            dispatcher.VerifyAccess();
            if (disposed) throw new ObjectDisposedException(nameof(ThemeService));
            ApplyAccent();
            resources.MergedDictionaries[0] = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/" + name + ".xaml")
            };
            resources["SwiftExportBackground"] = contrast ? SystemColors.WindowBrush : Brush(250, 250, 250);
            resources["SwiftExportForeground"] = contrast ? SystemColors.WindowTextBrush : Brush(32, 32, 32);
            resources["SwiftExportError"] = contrast ? SystemColors.WindowTextBrush : Brush(180, 35, 24);
            // Light 的文字别名使用 StaticResource，不会跟随官方根颜色变化；在项目层明确同步。
            var foreground = contrast ? SystemColors.HighlightTextBrush
                : new SolidColorBrush((Color)applicationResources["TextOnAccentFillColorPrimary"]);
            resources["AccentButtonForeground"] = foreground;
            resources["AccentButtonForegroundPointerOver"] = foreground;
            resources["AccentButtonForegroundPressed"] = contrast ? SystemColors.HighlightTextBrush
                : new SolidColorBrush((Color)applicationResources["TextOnAccentFillColorSecondary"]);
            foreach (var key in new[] { "AccentButtonBackground", "AccentButtonBackgroundPointerOver", "AccentButtonBackgroundPressed" })
            {
                if (contrast) resources[key] = SystemColors.HighlightBrush;
                else resources.Remove(key);
            }
            foreach (var key in new[] { "ButtonBackgroundDisabled", "ButtonForegroundDisabled", "ButtonBorderBrushDisabled", "AccentControlElevationBorderBrush" })
            {
                if (contrast) resources[key] = key == "ButtonBackgroundDisabled" ? SystemColors.WindowBrush : SystemColors.GrayTextBrush;
                else resources.Remove(key);
            }
        }

        // 官方管理器写入 Application 根资源。记录实际变更及原值，卸载时不覆盖后来者的写入。
        private void ApplyAccent()
        {
            var before = new Dictionary<object, object>();
            foreach (DictionaryEntry item in applicationResources) before[item.Key] = item.Value;
            try
            {
                ApplicationAccentColorManager.Apply(ApplicationAccentColorManager.GetColorizationColor(), ApplicationTheme.Light);
            }
            finally
            {
                foreach (DictionaryEntry item in applicationResources)
                {
                    object previous;
                    if (!before.TryGetValue(item.Key, out previous)) previous = Missing;
                    if (ReferenceEquals(previous, item.Value)) continue;
                    if (!originals.ContainsKey(item.Key)) originals[item.Key] = previous;
                    written[item.Key] = item.Value;
                }
            }
        }

        // 仅恢复仍由本服务持有的根资源键；合并字典中的同名键从未被删除。
        private void RestoreAccentResources()
        {
            foreach (var item in written)
            {
                if (!ReferenceEquals(applicationResources[item.Key], item.Value)) continue;
                var original = originals[item.Key];
                if (ReferenceEquals(original, Missing)) applicationResources.Remove(item.Key);
                else applicationResources[item.Key] = original;
            }
            written.Clear();
            originals.Clear();
        }

        private static SolidColorBrush Brush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        // 还原 WPF-UI 的线程级资源承载；保留后来者显式替换的资源入口。
        private void RestoreUiResources()
        {
            var ui = Wpf.Ui.UiApplication.Current;
            if (ReferenceEquals(ui.Resources, applicationResources)) ui.Resources = previousUiResources;
            ui.ApplySystemAccentColor = previousAutoAccent;
        }

        /// <summary>在所属 STA 解绑静态事件并恢复自有强调色写入，排队通知不再访问资源。</summary>
        public void Dispose()
        {
            dispatcher.VerifyAccess();
            if (disposed) return;
            disposed = true;
            SystemParameters.StaticPropertyChanged -= SystemChanged;
            RestoreAccentResources();
            RestoreUiResources();
        }
    }
}

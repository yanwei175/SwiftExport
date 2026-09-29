using System;
using System.Collections;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Wpf.Ui.Appearance;

namespace SwiftExport.Client.VSTO.UI.Themes
{
    /// <summary>初始化插件固定 Light 和强调色；不订阅系统主题切换，不改变 Office 主题。</summary>
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
        public ThemeService(ResourceDictionary resources, Dispatcher dispatcher, bool initializeApplicationTheme = false)
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
            try { InitializeLight(initializeApplicationTheme); }
            catch { RestoreAccentResources(); RestoreUiResources(); throw; }
        }

        // 官方 Apply 负责自建 Application 的主题状态；借用宿主时只替换插件自己的 Light 字典。
        private void InitializeLight(bool initializeApplicationTheme)
        {
            // 先检查字典结构，失败交由构造函数恢复 WPF-UI 入口。
            var currentTheme = resources.MergedDictionaries[0];
            if (initializeApplicationTheme)
                ApplyAccent(() => ApplicationThemeManager.Apply(ApplicationTheme.Light, Wpf.Ui.Controls.WindowBackdropType.None));
            else
            {
                resources.MergedDictionaries[0] = new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Light.xaml")
                };
                ApplyAccent();
            }
            resources["SwiftExportBackground"] = Brush(250, 250, 250);
            resources["SwiftExportForeground"] = Brush(32, 32, 32);
            resources["SwiftExportError"] = Brush(180, 35, 24);
            // Light 的文字别名使用 StaticResource；在项目层同步官方强调色文字，避免主色按钮文字失配。
            var foreground = new SolidColorBrush((Color)applicationResources["TextOnAccentFillColorPrimary"]);
            resources["AccentButtonForeground"] = foreground;
            resources["AccentButtonForegroundPointerOver"] = foreground;
            resources["AccentButtonForegroundPressed"] = new SolidColorBrush((Color)applicationResources["TextOnAccentFillColorSecondary"]);
        }

        // 官方管理器写入 Application 根资源。记录实际变更及原值，卸载时不覆盖后来者的写入。
        private void ApplyAccent(Action initializeTheme = null)
        {
            var before = new Dictionary<object, object>();
            foreach (DictionaryEntry item in applicationResources) before[item.Key] = item.Value;
            try
            {
                if (initializeTheme != null) initializeTheme();
                else ApplicationAccentColorManager.Apply(ApplicationAccentColorManager.GetColorizationColor(), ApplicationTheme.Light);
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

        /// <summary>在所属 STA 恢复自有强调色和 WPF-UI 入口，不关闭 Application。</summary>
        public void Dispose()
        {
            dispatcher.VerifyAccess();
            if (disposed) return;
            disposed = true;
            RestoreAccentResources();
            RestoreUiResources();
        }
    }
}

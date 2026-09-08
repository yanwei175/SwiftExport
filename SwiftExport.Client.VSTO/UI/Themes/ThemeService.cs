using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace SwiftExport.Client.VSTO.UI.Themes
{
    /// <summary>维护插件颜色并响应系统高对比度；不改变 Office 的主题。</summary>
    public sealed class ThemeService : IDisposable
    {
        private readonly ResourceDictionary resources;
        private readonly Dispatcher dispatcher;
        private bool disposed;
        /// <summary>在资源所属 UI 线程初始化并订阅系统颜色变化。</summary>
        public ThemeService(ResourceDictionary resources, Dispatcher dispatcher)
        {
            this.resources = resources; this.dispatcher = dispatcher;
            Apply();
            SystemParameters.StaticPropertyChanged += SystemChanged;
        }
        // 系统事件不保证来源线程，且排队后可能已经释放。
        private void SystemChanged(object sender, PropertyChangedEventArgs args)
        {
            if (!disposed && !dispatcher.HasShutdownStarted)
                dispatcher.BeginInvoke(new Action(() => { if (!disposed) Apply(); }));
        }
        private void Apply()
        {
            var contrast = SystemParameters.HighContrast;
            resources["SwiftExportBackground"] = contrast ? SystemColors.WindowBrush : new SolidColorBrush(Color.FromRgb(250, 250, 250));
            resources["SwiftExportForeground"] = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(32, 32, 32));
            resources["SwiftExportError"] = contrast ? SystemColors.WindowTextBrush : new SolidColorBrush(Color.FromRgb(180, 35, 24));
        }
        /// <summary>解除静态事件，排队中的通知不再访问资源。</summary>
        public void Dispose() { if (disposed) return; disposed = true; SystemParameters.StaticPropertyChanged -= SystemChanged; }
    }
}

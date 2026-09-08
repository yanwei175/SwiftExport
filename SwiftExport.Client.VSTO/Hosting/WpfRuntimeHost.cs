using System;
using System.Windows;

namespace SwiftExport.Client.VSTO.Hosting
{
    /// <summary>承载插件 WPF 资源，不拥有 Excel 消息循环的关闭权。</summary>
    public sealed class WpfRuntimeHost : IDisposable
    {
        private ResourceDictionary resources;
        private UI.Themes.ThemeService theme;
        /// <summary>在 Excel STA 初始化一次资源，不创建第二个消息循环。</summary>
        public void Initialize()
        {
            if (resources != null) return;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            app.Dispatcher.VerifyAccess();
            resources = new ResourceDictionary { Source = new Uri("/SwiftExport.Client.VSTO;component/Resources/Themes.xaml", UriKind.Relative) };
            app.Resources.MergedDictionaries.Add(resources);
            theme = new UI.Themes.ThemeService(resources, app.Dispatcher);
        }
        /// <summary>仅移除自己加入的字典，保留宿主 Dispatcher 和其他资源。</summary>
        public void Dispose()
        {
            theme?.Dispose();
            if (resources != null) Application.Current?.Resources.MergedDictionaries.Remove(resources);
            resources = null;
        }
    }
}

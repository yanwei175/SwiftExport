using System;
using System.Windows;

namespace SwiftExport.Client.VSTO.Hosting
{
    /// <summary>承载插件 WPF 资源，不拥有 Excel 消息循环的关闭权。</summary>
    public sealed class WpfRuntimeHost : IDisposable
    {
        private ResourceDictionary resources;
        private UI.Themes.ThemeService theme;
        private Application application;
        private bool disposed;
        /// <summary>在 Excel STA 初始化一次资源，不创建第二个消息循环。</summary>
        public void Initialize()
        {
            if (disposed) throw new ObjectDisposedException(nameof(WpfRuntimeHost));
            application?.Dispatcher.VerifyAccess();
            if (resources != null) return;
            var ownsApplication = Application.Current == null;
            var app = Application.Current ?? new App();

            app.Dispatcher.VerifyAccess();

            // 自建 Application 的字典已由 InitializeComponent 加载，不再合并第二份。
            // 借用其他宿主的 Application 时只添加和卸载本插件字典，不替换其资源根。
            var dictionary = ownsApplication ? app.Resources.MergedDictionaries[0] : new ResourceDictionary
            {
                Source = new Uri("/" + typeof(WpfRuntimeHost).Assembly.GetName().Name + ";component/Resources/Themes.xaml", UriKind.Relative)
            };
            if (!ownsApplication) app.Resources.MergedDictionaries.Add(dictionary);
            try
            {
                theme = new UI.Themes.ThemeService(dictionary, app.Dispatcher, ownsApplication);
                application = app;
                resources = dictionary;
            }
            catch
            {
                // 失败不留下半初始化字典；Application 仍由宿主 AppDomain 承载。
                app.Resources.MergedDictionaries.Remove(dictionary);
                throw;
            }
        }
        /// <summary>仅移除自己加入的字典，保留宿主 Dispatcher 和其他资源。</summary>
        public void Dispose()
        {
            application?.Dispatcher.VerifyAccess();
            if (disposed) return;
            disposed = true;
            theme?.Dispose();
            if (resources != null)
                application.Resources.MergedDictionaries.Remove(resources);
            resources = null;
            theme = null;
        }
    }
}

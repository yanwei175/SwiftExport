using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace MateThemeFWTest
{
    /// <summary>
    /// App.xaml 的交互逻辑
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 中文注释：测试宿主启动时注入控件库 Dispatcher。
            MateWpfThemes.FW.DispatcherProvider.Initialize(Dispatcher.CurrentDispatcher);
        }
    }
}

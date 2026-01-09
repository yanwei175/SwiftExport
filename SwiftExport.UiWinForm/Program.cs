using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwiftExport.AppLayer.DI;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.AppLayer.Services;
using SwiftExport.UI.Framework.AppForms;
using SwiftExport.UiWinForm.DI;

namespace SwiftExport.UiWinForm
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.          
            ApplicationConfiguration.Initialize();

            // 从 DI 获取主窗体
            var mainFrm = DI_WinFormHost.GetService<MainFrm>();

            // 注入 WinForm 的 Invoke 逻辑
            SwiftExport.UI.Framework.Common.ThreadHelper.MainThreadDispatcher = (action) =>
            {
                if (mainFrm.InvokeRequired) mainFrm.Invoke(action);
                else action();
            };

            Application.Run(mainFrm);

            //Application.Run(provider.GetRequiredService<LoginFrm>();
        }

    }
}
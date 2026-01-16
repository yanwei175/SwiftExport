using SwiftExport.UI.Services; // 你的 GlobalApi 命名空间
using SwiftExport.UI.Views; // 你的 WPF 窗口命名空间
using System.Windows;
using System.Windows.Interop;

namespace SwiftExport.UI;

public static class AppStarter
{
    private static MainWindow _mainWindow;

    // ownerHwnd: 父窗口句柄 (Excel 传 Excel的句柄，桌面端传 IntPtr.Zero)
    public static void ShowMain(IntPtr ownerHwnd)
    {
        // 1. 确保 API 已初始化
        if (GlobalApi.Client == null)
        {
            GlobalApi.Initialize();
        }

        // 2. 窗口单例逻辑
        if (_mainWindow == null || !_mainWindow.IsLoaded)
        {
            _mainWindow = new MainWindow();

            // 🌟 关键：如果是 Excel 调用的，就设置 Owner
            if (ownerHwnd != IntPtr.Zero)
            {
                var helper = new WindowInteropHelper(_mainWindow);
                helper.Owner = ownerHwnd;
            }
            // 如果是桌面端 (ownerHwnd == 0)，就默认居中显示，不用设 Owner

            _mainWindow.Closed += (s, e) => _mainWindow = null;
            _mainWindow.Show();
        }
        else
        {
            if (_mainWindow.WindowState == WindowState.Minimized)
                _mainWindow.WindowState = WindowState.Normal;

            _mainWindow.Activate();
        }
    }
}
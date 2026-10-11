using Velopack;

namespace SwiftExportClient;

/// <summary>先处理安装器事件，再启动 WPF；更新仅在用户确认后应用。</summary>
public static class Program
{
    /// <summary>提供 STA 入口，避免安装及更新回调启动登录、网络和业务窗口。</summary>
    [STAThread]
    public static void Main()
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Shell;

namespace SwiftExportClient;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = AppHost.Build();
        await _host.StartAsync();

        var shellWindow = _host.Services.GetRequiredService<ShellWindow>();
        MainWindow = shellWindow;
        shellWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}

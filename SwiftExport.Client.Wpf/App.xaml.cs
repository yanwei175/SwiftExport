using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Shell;
using SwiftExportClient.Updates;

namespace SwiftExportClient;

public partial class App : Application
{
    private IHost? _host;
    private bool _switchingSession;

    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        _host = AppHost.Build();
        await _host.StartAsync();

        var versionCheck = await _host.Services.GetRequiredService<IClientVersionService>().CheckAsync();
        if (!versionCheck.CanContinue)
        {
            MessageBox.Show(
                BuildUpdateMessage(versionCheck),
                "SwiftExport Client",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            Shutdown();
            return;
        }

        if (versionCheck.Requirement is UpdateRequirement.Optional)
        {
            MessageBox.Show(
                BuildUpdateMessage(versionCheck),
                "SwiftExport Client",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        var authService = _host.Services.GetRequiredService<IAuthService>();
        var restored = await authService.TryRestoreSessionAsync();
        var sessionService = _host.Services.GetRequiredService<ISessionService>();
        sessionService.SessionEnded += OnSessionEnded;

        if (!restored && !ShowLoginWindow())
        {
            Shutdown();
            return;
        }

        ShowShellWindow();
    }

    private bool ShowLoginWindow()
    {
        if (_host is null)
        {
            return false;
        }

        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        MainWindow = loginWindow;
        return loginWindow.ShowDialog() == true;
    }

    private void ShowShellWindow()
    {
        if (_host is null)
        {
            return;
        }

        var shellWindow = _host.Services.GetRequiredService<ShellWindow>();
        shellWindow.Closed += OnShellWindowClosed;
        MainWindow = shellWindow;
        shellWindow.Show();
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (_switchingSession)
            {
                return;
            }

            _switchingSession = true;
            try
            {
                if (MainWindow is ShellWindow shellWindow)
                {
                    shellWindow.Closed -= OnShellWindowClosed;
                    shellWindow.Close();
                }

                if (!ShowLoginWindow())
                {
                    Shutdown();
                    return;
                }

                ShowShellWindow();
            }
            finally
            {
                _switchingSession = false;
            }
        });
    }

    private void OnShellWindowClosed(object? sender, EventArgs e)
    {
        if (!_switchingSession)
        {
            Shutdown();
        }
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

    private static string BuildUpdateMessage(ClientVersionCheckResult versionCheck)
    {
        var message = versionCheck.Message;
        var versionInfo = versionCheck.VersionInfo;

        if (versionInfo is null)
        {
            return message;
        }

        if (!string.IsNullOrWhiteSpace(versionInfo.ReleaseNotes))
        {
            message += $"{Environment.NewLine}{Environment.NewLine}{versionInfo.ReleaseNotes}";
        }

        if (!string.IsNullOrWhiteSpace(versionInfo.DownloadUrl))
        {
            message += $"{Environment.NewLine}{Environment.NewLine}下载地址：{versionInfo.DownloadUrl}";
        }

        return message;
    }
}

using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SwiftExportClient.Authentication;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Shell;
using SwiftExportClient.Services.Images;
using SwiftExportClient.Updates;
using SwiftExportClient.Resources.Controls;
using SwiftExportClient.Services.Caching;

namespace SwiftExportClient;

/// <summary>启动应用服务、最低版本校验及登录会话，协调进程级更新提示。</summary>
public partial class App : Application
{
    private IHost? _host;
    private bool _switchingSession;

    /// <summary>先检查兼容性，再启动业务窗口并检查可用更新。</summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        base.OnStartup(e);

        _host = AppHost.Build();
        await _host.StartAsync();
        Resources[CachedImage.ImageCacheServiceResourceKey] =
            _host.Services.GetRequiredService<IImageCacheService>();
        Resources[CachedImage.ImageAddressResolverResourceKey] =
            _host.Services.GetRequiredService<IImageAddressResolver>();

        var versionCheck = await _host.Services.GetRequiredService<IClientVersionService>().CheckAsync();
        if (!versionCheck.CanContinue)
        {
            // 最低版本拦截先提供真实下载安装入口；关闭后仍不允许旧客户端登录。
            _host.Services.GetRequiredService<ClientUpdateDialogService>()
                .Show(requirementMessage: BuildUpdateMessage(versionCheck));
            Shutdown();
            return;
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
        _ = WarmSystemParametersAsync();
        // 每次进程启动检查一次；没有更新时不打断登录或工作。
        var updates = _host.Services.GetRequiredService<ClientUpdateViewModel>();
        await updates.CheckOnStartupAsync();
        if (updates.AvailableRelease is not null && MainWindow is ShellWindow shell && shell.IsVisible)
        {
            _host.Services.GetRequiredService<SwiftExportClient.Services.UI.IMessageService>()
                .ShowInfo("客户端更新", updates.Message);
        }
    }

    private bool ShowLoginWindow()
    {
        if (_host is null)
        {
            return false;
        }

        var loginWindow = _host.Services.GetRequiredService<LoginWindow>();
        MainWindow = loginWindow;
        loginWindow.ShowDialog();
        return loginWindow.IsLoginSuccessful;
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

    /// <summary>回 UI 线程重建会话；新会话参数加载失败不阻断业务窗口。</summary>
    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
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
                _ = WarmSystemParametersAsync();
            }
            finally
            {
                _switchingSession = false;
            }
        });
    }

    /// <summary>预热独立参数缓存，失败允许登录后从个人设置手动刷新。</summary>
    private async Task WarmSystemParametersAsync()
    {
        if (_host is null) return;
        var user = _host.Services.GetRequiredService<ICurrentUserService>();
        var userId = user.CurrentUser?.UserId;
        var result = await _host.Services.GetRequiredService<ISystemParameterCacheService>().EnsureLoadedAsync();
        if (!result.Success && user.IsAuthenticated && user.CurrentUser?.UserId == userId)
            _host.Services.GetRequiredService<SwiftExportClient.Services.UI.IMessageService>()
                .ShowWarning("系统参数未加载", result.Message);
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

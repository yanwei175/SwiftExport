using Microsoft.Extensions.DependencyInjection;
using SwiftExportClient.Authentication;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;
using SwiftExportClient.Features.SystemSettings.Passwords.Views;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;
using SwiftExportClient.Features.SystemSettings.UserRegistration.ViewModels;
using SwiftExportClient.Features.SystemSettings.Passwords.ViewModels;
using SwiftExportClient.Features.SystemSettings.Workspace.Views;
using System.Windows;
using Wpf.Ui;

namespace SwiftExportClient.Services.UI;

/// <summary>解析个人改密与单实例系统设置窗口，设置 Shell Owner 并在会话结束时清理。</summary>
public sealed class SettingsWindowService : ISettingsWindowService, IDisposable
{
    private const string ChangePasswordKey = "change-password";
    private const string SystemSettingsKey = "system-settings";

    private readonly IServiceProvider _serviceProvider;
    private readonly ICurrentUserService _currentUserService;
    private readonly ISessionService _sessionService;
    private readonly IMessageService _messageService;
    private readonly Dictionary<string, Window> _openWindows = new(StringComparer.Ordinal);
    private bool _isDisposed;

    /// <summary>创建设置窗口编排服务，并订阅会话结束以清理旧会话窗口。</summary>
    public SettingsWindowService(
        IServiceProvider serviceProvider,
        ICurrentUserService currentUserService,
        ISessionService sessionService,
        IMessageService messageService)
    {
        _serviceProvider = serviceProvider;
        _currentUserService = currentUserService;
        _sessionService = sessionService;
        _messageService = messageService;
        _sessionService.SessionEnded += OnSessionEnded;
    }

    /// <inheritdoc />
    public void ShowChangePassword()
    {
        if (!_currentUserService.IsAuthenticated)
        {
            _messageService.ShowWarning("设置", "请先登录后再修改密码。");
            return;
        }

        ShowWindow(ChangePasswordKey, () => _serviceProvider.GetRequiredService<ChangePasswordWindow>(), modal: true);
    }

    /// <inheritdoc />
    public void ShowSystemSettings()
    {
        if (!new[] { "System.Users.View", "System.Roles.View", "System.Permissions.View", "System.Dictionary.View" }
            .Any(_currentUserService.HasPermission))
        {
            _messageService.ShowWarning("设置", "当前账号没有可访问的系统设置页面。");
            return;
        }
        ShowWindow(SystemSettingsKey, CreateSystemSettingsWindow, modal: false);
    }

    /// <summary>个人改密仍为模态；系统窗口为非模态且重复点击只激活已有实例。</summary>
    private void ShowWindow(string key, Func<Window> factory, bool modal)
    {
        if (_openWindows.TryGetValue(key, out var existing))
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        var owner = Application.Current?.MainWindow;
        if (owner is null)
        {
            _messageService.ShowError("设置", "当前找不到可用的 Shell 窗口。");
            return;
        }

        var window = factory();
        window.Owner = owner;
        _openWindows.Add(key, window);
        void RemoveWindow(object? sender, EventArgs args)
        {
            window.Closed -= RemoveWindow;
            _openWindows.Remove(key);
        }

        window.Closed += RemoveWindow;
        if (!modal)
        {
            window.Show();
            return;
        }
        try
        {
            window.ShowDialog();
        }
        finally
        {
            window.Closed -= RemoveWindow;
            _openWindows.Remove(key);
        }
    }

    private Window CreateSystemSettingsWindow()
    {
        IContentDialogService contentDialogs = new ContentDialogService();
        var localDialogService = new WpfUiDialogService(contentDialogs);
        var localMessageService = new WindowMessageService();
        return new SystemSettingsWindow(
            () => ActivatorUtilities.CreateInstance<RbacManagementViewModel>(_serviceProvider, localDialogService),
            () => ActivatorUtilities.CreateInstance<DictionaryManagerViewModel>(
                _serviceProvider, localDialogService, localMessageService),
            () => ActivatorUtilities.CreateInstance<UserRegistrationViewModel>(_serviceProvider, localMessageService),
            () => ActivatorUtilities.CreateInstance<ResetPasswordViewModel>(_serviceProvider, localMessageService),
            _currentUserService, contentDialogs, localMessageService);
    }

    private void OnSessionEnded(object? sender, SessionEndedEventArgs e)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(CloseAllWindows);
            return;
        }

        CloseAllWindows();
    }

    private void CloseAllWindows()
    {
        foreach (var window in _openWindows.Values.ToArray())
        {
            if (window is IManagedSettingsWindow managedWindow)
            {
                managedWindow.ForceClose();
            }
            else
            {
                window.Close();
            }
        }

        _openWindows.Clear();
    }

    /// <summary>解除会话订阅，并关闭仍属于当前会话的所有设置窗口。</summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _sessionService.SessionEnded -= OnSessionEnded;
        CloseAllWindows();
    }
}

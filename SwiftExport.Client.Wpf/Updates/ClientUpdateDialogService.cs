using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Updates;

/// <summary>负责更新窗口的创建、所有权及重复打开激活，不修改下载状态。</summary>
public sealed class ClientUpdateDialogService(ClientUpdateViewModel viewModel, IContentDialogService dialogs)
{
    private ClientUpdateWindow? _window;

    /// <summary>打开唯一模态窗口；启动最低版本拦截时允许没有 Shell 所有者。</summary>
    public void Show(Window? owner = null, string? requirementMessage = null, ContentDialogHost? restoreHost = null)
    {
        if (_window is not null) { _window.Activate(); return; }
        _window = new ClientUpdateWindow(viewModel, requirementMessage) { Owner = owner };
        dialogs.SetDialogHost(_window.DialogHost);
        try { _window.ShowDialog(); }
        finally
        {
            if (restoreHost is not null) dialogs.SetDialogHost(restoreHost);
            _window = null;
        }
    }
}

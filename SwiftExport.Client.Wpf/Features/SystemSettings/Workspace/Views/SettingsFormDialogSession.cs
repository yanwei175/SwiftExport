using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Services.UI;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>仅为系统设置表单承载本地 ContentDialog；放弃确认在同一弹窗内完成。</summary>
internal sealed class SettingsFormDialogSession : IDisposable
{
    private readonly IContentDialogService _host;
    private readonly ContentDialog _dialog;
    private readonly Border _discardPrompt;
    private readonly Func<bool> _hasDraft;
    private readonly Func<bool> _isBusy;
    private readonly Action _discard;
    private readonly Action _cleanup;
    private bool _completed;
    private bool _forced;
    private bool _disposed;

    /// <summary>撤权或会话切换后阻止迟到的保存回调再交还创建结果。</summary>
    public bool WasForced => _forced || _disposed;

    /// <summary>表单自己负责保存，宿主只控制取消、成功和强制撤权关闭。</summary>
    public SettingsFormDialogSession(IContentDialogService host, string title, FrameworkElement form,
        Func<bool> hasDraft, Func<bool> isBusy, Action discard, Action cleanup)
    {
        _host = host;
        _hasDraft = hasDraft;
        _isBusy = isBusy;
        _discard = discard;
        _cleanup = cleanup;
        var content = new Grid { MaxHeight = 620 };
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.Children.Add(form);
        var keep = new System.Windows.Controls.Button { Content = "继续编辑", Margin = new Thickness(0, 0, 8, 0) };
        keep.Click += (_, _) => _discardPrompt!.Visibility = Visibility.Collapsed;
        var abandon = new System.Windows.Controls.Button { Content = "放弃变更" };
        abandon.Click += (_, _) =>
        {
            if (_isBusy()) return;
            _forced = true;
            _discard();
            _dialog!.Hide(ContentDialogResult.None);
        };
        var actions = new StackPanel { Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right };
        actions.Children.Add(keep);
        actions.Children.Add(abandon);
        var prompt = new StackPanel();
        prompt.Children.Add(new System.Windows.Controls.TextBlock { Text = "表单有未保存变更，确定放弃吗？",
            Margin = new Thickness(0, 0, 0, 8) });
        prompt.Children.Add(actions);
        _discardPrompt = new Border { Child = prompt, Padding = new Thickness(12),
            Margin = new Thickness(12), Visibility = Visibility.Collapsed };
        Grid.SetRow(_discardPrompt, 1);
        content.Children.Add(_discardPrompt);
        _dialog = new ContentDialog { Title = title, Content = content, CloseButtonText = "取消" };
        _dialog.Closing += OnClosing;
    }

    /// <summary>显示系统设置窗口自己的 ContentDialogHost，结束后释放表单及密码。</summary>
    public async Task ShowAsync()
    {
        try { await _host.ShowAsync(_dialog, CancellationToken.None); }
        finally { Dispose(); }
    }

    /// <summary>表单内取消与弹窗取消走同一关闭门禁。</summary>
    public void RequestCancel() => _dialog.Hide(ContentDialogResult.None);

    /// <summary>仅在对应保存确实成功后结束弹窗。</summary>
    public void Complete()
    {
        if (_disposed || _forced) return;
        _completed = true;
        _dialog.Hide(ContentDialogResult.Primary);
    }

    /// <summary>会话切换或撤权绕过确认并立即擦除敏感输入。</summary>
    public void ForceClose()
    {
        if (_disposed) return;
        _forced = true;
        _discard();
        _dialog.Hide(ContentDialogResult.None);
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs e)
    {
        if (_forced || _completed) return;
        e.Cancel = true;
        if (_isBusy()) return;
        if (!_hasDraft())
        {
            _forced = true;
            _dialog.Dispatcher.BeginInvoke(new Action(() => _dialog.Hide(ContentDialogResult.None)));
            return;
        }
        _discardPrompt.Visibility = Visibility.Visible;
    }

    /// <summary>解除 WPF-UI 事件并清理表单，避免迟到响应触及已关闭内容。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dialog.Closing -= OnClosing;
        _cleanup();
        _dialog.Content = null;
    }
}

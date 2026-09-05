using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Services.UI;
using System.ComponentModel;
using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Features.SystemSettings.Dictionaries.Views;

/// <summary>为可复用字典 View 提供独立模态宿主、本地消息和未保存关闭门禁。</summary>
public partial class DictionaryManagerWindow : FluentWindow, IManagedSettingsWindow
{
    private readonly DictionaryManagerViewModel _viewModel;
    private readonly IContentDialogService _contentDialogService;
    private readonly WindowMessageService _messageService;
    private bool _allowClose;
    private bool _checkingClose;

    /// <summary>创建字典模态窗口，并注入窗口本地确认与消息服务。</summary>
    public DictionaryManagerWindow(
        DictionaryManagerViewModel viewModel,
        IContentDialogService contentDialogService,
        WindowMessageService messageService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _contentDialogService = contentDialogService;
        _messageService = messageService;
        DataContext = viewModel;
        Loaded += OnLoaded;
        Closing += OnClosing;
        Closed += OnClosed;
        _messageService.MessageChanged += OnMessageChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _contentDialogService.SetDialogHost(LocalDialogHost);
        await _viewModel.InitializeAsync();
        DictionaryView.Focus();
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_checkingClose || _viewModel.IsWriting)
        {
            return;
        }

        _checkingClose = true;
        try
        {
            DictionaryView.CommitPendingEdits();
            if (await _viewModel.CanCloseAsync(CancellationToken.None))
            {
                _allowClose = true;
                _viewModel.Dispose();
                Close();
            }
        }
        finally
        {
            _checkingClose = false;
        }
    }

    private void OnMessageChanged(object? sender, StatusMessageChangedEventArgs e)
    {
        WindowInfoBar.Title = e.Title ?? string.Empty;
        WindowInfoBar.Message = e.Message ?? string.Empty;
        WindowInfoBar.Severity = e.Severity switch
        {
            UiMessageSeverity.Success => InfoBarSeverity.Success,
            UiMessageSeverity.Warning => InfoBarSeverity.Warning,
            UiMessageSeverity.Error => InfoBarSeverity.Error,
            _ => InfoBarSeverity.Informational
        };
        WindowInfoBar.IsOpen = e.IsOpen;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _viewModel.Dispose();
        _messageService.MessageChanged -= OnMessageChanged;
    }

    /// <inheritdoc />
    public void ForceClose()
    {
        _allowClose = true;
        _viewModel.Dispose();
        Close();
    }
}

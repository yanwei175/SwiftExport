using System.Windows;
using Wpf.Ui;
using Wpf.Ui.Controls;
using SwiftExportClient.Updates;

namespace SwiftExportClient.Shell;

/// <summary>承载导航页签与对话框宿主，负责窗口级交互和资源释放。</summary>
public partial class ShellWindow : FluentWindow
{
    private readonly IContentDialogService _contentDialogService;
    private readonly ISnackbarService _snackbarService;
    private readonly ShellViewModel _viewModel;
    private readonly ClientUpdateDialogService _updates;

    /// <summary>绑定 Shell 状态及更新窗口服务。</summary>
    public ShellWindow(
        ShellViewModel viewModel,
        IContentDialogService contentDialogService,
        ISnackbarService snackbarService,
        ClientUpdateDialogService updates)
    {
        _contentDialogService = contentDialogService;
        _snackbarService = snackbarService;
        _viewModel = viewModel;
        _updates = updates;
        InitializeComponent();
        DataContext = viewModel;
        Closed += OnClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _contentDialogService.SetDialogHost(RootContentDialogHost);
        _snackbarService.SetSnackbarPresenter(RootSnackbarPresenter);
        await _viewModel.InitializeAsync();
    }

    private void OnCheckUpdatesClick(object sender, RoutedEventArgs e) => ShowUpdates();

    /// <summary>暂时把离开确认宿主交给更新窗口，关闭后恢复当前 Shell 宿主。</summary>
    public void ShowUpdates() => _updates.Show(this, restoreHost: RootContentDialogHost);

    /// <summary>窗口关闭时释放 ViewModel 的会话、导航和状态消息订阅。</summary>
    private void OnClosed(object? sender, EventArgs e)
    {
        Closed -= OnClosed;
        _viewModel.Dispose();
    }

}

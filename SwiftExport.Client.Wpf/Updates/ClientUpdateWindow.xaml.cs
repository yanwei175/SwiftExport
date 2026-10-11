using System.ComponentModel;
using System.Windows;
using Wpf.Ui.Controls;

namespace SwiftExportClient.Updates;

/// <summary>承载更新状态，模态期间冻结主窗口交互，关闭时取消可取消的更新请求。</summary>
public partial class ClientUpdateWindow : FluentWindow
{
    private readonly ClientUpdateViewModel _viewModel;

    /// <summary>承载页面离开确认，确保模态更新窗口打开时确认按钮仍可操作。</summary>
    public ContentDialogHost DialogHost => UpdateDialogHost;

    /// <summary>绑定应用级状态，可附加后端最低版本限制说明。</summary>
    public ClientUpdateWindow(ClientUpdateViewModel viewModel, string? requirementMessage = null)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        RequirementText.Text = requirementMessage ?? "更新下载后由你选择重启时间。";
    }

    private async void OnLoaded(object sender, RoutedEventArgs e) => await _viewModel.CheckCommand.ExecuteAsync(null);
    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_viewModel.IsRestarting) { e.Cancel = true; return; }
        _viewModel.CancelCurrentOperation();
    }
}

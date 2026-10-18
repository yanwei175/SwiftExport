using System.Windows.Controls;
using System.Windows;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.Models;
using SwiftExportClient.Features.DemandPlanning.GrossDemand.ViewModels;

namespace SwiftExportClient.Features.DemandPlanning.GrossDemand.Views;

/// <summary>承载成品需求工作台交互，并在保活页签隐藏时关闭页面级筛选浮层。</summary>
public partial class GrossDemandWorkspaceView : UserControl
{
    /// <summary>初始化成品需求工作台视图及其可见性生命周期处理。</summary>
    public GrossDemandWorkspaceView()
    {
        InitializeComponent();
        IsVisibleChanged += OnIsVisibleChanged;
    }

    /// <summary>页签隐藏时关闭多行筛选 Popup，不清理用户已经输入的筛选条件。</summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false && DataContext is GrossDemandWorkspaceViewModel viewModel)
        {
            viewModel.IsMultiLineFilterOpen = false;
        }
    }

    private async void OnSourceGroupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is GrossDemandWorkspaceViewModel viewModel
            && sender is ListBox { SelectedItem: GrossDemandSourceGroupModel group })
        {
            await viewModel.SelectSourceGroupCommand.ExecuteAsync(group);
        }
    }
}

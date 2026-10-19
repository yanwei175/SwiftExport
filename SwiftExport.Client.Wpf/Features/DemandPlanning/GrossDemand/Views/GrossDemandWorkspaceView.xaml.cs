using System.Windows.Controls.Primitives;
using System.Windows.Input;
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
    /// <summary>只调整来源面板视觉宽度，不触发查询或改变已应用来源。</summary>
    private void OnSourceResizeDelta(object sender, DragDeltaEventArgs e) => SourcePanel.Width = Math.Clamp(SourcePanel.Width + e.HorizontalChange, 160, 440);
    /// <summary>右击明确当前行，复制仍可使用表格原生多选。</summary>
    private void OnDemandRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow { Item: GrossDemandRowModel row })
        {
            var retained = grid.SelectedItems.Contains(row) ? grid.SelectedItems.Cast<object>().ToArray() : [];
            grid.SelectedItem = row;
            foreach (var selected in retained) if (!grid.SelectedItems.Contains(selected)) grid.SelectedItems.Add(selected);
        }
    }
    /// <summary>双击真实需求行按独立维护权限进入编辑，不将双击表头视作业务操作。</summary>
    private async void OnDemandDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid grid && e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(grid, source) is DataGridRow { Item: GrossDemandRowModel row }
            && DataContext is GrossDemandWorkspaceViewModel vm)
        { vm.SelectedItem = row; if (vm.EditSelectedManualCommand.CanExecute(null)) await vm.EditSelectedManualCommand.ExecuteAsync(null); }
    }}

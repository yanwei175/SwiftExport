using System.Windows.Controls;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;
using System.Windows;
using System.Windows.Input;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using System.Windows.Threading;

namespace SwiftExportClient.Features.OrderTracking.DemandDetail.Views;

/// <summary>承载成品需求基础事实的只读明细布局。</summary>
public partial class DemandTrackingDetailView : UserControl
{
    /// <summary>初始化基础明细视图。</summary>
    public DemandTrackingDetailView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>视图重建后按VM保留的区域加载，不让未显示时的默认选择覆盖保活状态。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e) => await LoadVisibleSectionAsync();

    /// <summary>区域页签首次激活才读取，行选择事件不会重复触发加载。</summary>
    private async void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SectionsTabs) && IsLoaded) await LoadVisibleSectionAsync();
    }

    /// <summary>等待双向选择绑定完成后读取当前VM，并拒绝已卸载或被替换的视图上下文。</summary>
    private async Task LoadVisibleSectionAsync()
    {
        if (DataContext is not DemandTrackingDetailViewModel vm) return;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        if (IsLoaded && ReferenceEquals(vm, DataContext)) await vm.LoadSelectedSectionCommand.ExecuteAsync(null);
    }

    /// <summary>只双击真实物料行打开详情，历史删除行也可读取证据。</summary>
    private async void OnMaterialDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DemandTrackingDetailViewModel vm || e.OriginalSource is not DependencyObject source
            || ItemsControl.ContainerFromElement(MaterialsGrid, source) is not DataGridRow { Item: DemandTrackingMaterialDto material }) return;
        vm.SelectedMaterial = material;
        if (vm.OpenMaterialCommand.CanExecute(null)) await vm.OpenMaterialCommand.ExecuteAsync(null);
    }
}

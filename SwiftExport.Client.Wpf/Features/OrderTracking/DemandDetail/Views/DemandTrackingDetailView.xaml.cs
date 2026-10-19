using System.Windows.Controls;
using SwiftExportClient.Features.OrderTracking.DemandDetail.ViewModels;

namespace SwiftExportClient.Features.OrderTracking.DemandDetail.Views;

/// <summary>承载成品需求基础事实的只读明细布局。</summary>
public partial class DemandTrackingDetailView : UserControl
{
    /// <summary>初始化基础明细视图。</summary>
    public DemandTrackingDetailView() => InitializeComponent();

    /// <summary>区域页签首次激活才读取，行选择事件不会重复触发加载。</summary>
    private async void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SectionsTabs) && DataContext is DemandTrackingDetailViewModel vm)
        {
            vm.SelectedSectionIndex = SectionsTabs.SelectedIndex;
            await vm.LoadSelectedSectionCommand.ExecuteAsync(null);
        }
    }
}

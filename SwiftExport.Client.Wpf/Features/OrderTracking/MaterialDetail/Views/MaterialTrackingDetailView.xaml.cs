using System.Windows.Controls;
using SwiftExportClient.Features.OrderTracking.MaterialDetail.ViewModels;
using System.Windows;
using System.Windows.Threading;
namespace SwiftExportClient.Features.OrderTracking.MaterialDetail.Views;
/// <summary>承载独立物料快照与下游只读区域，不访问业务接口。</summary>
public partial class MaterialTrackingDetailView : UserControl
{
    /// <summary>初始化物料详情布局。</summary>
    public MaterialTrackingDetailView() { InitializeComponent(); Loaded += OnLoaded; }
    /// <summary>重建视图仍激活VM保留的区域，避免默认选择丢失原页签位置。</summary>
    private async void OnLoaded(object sender, RoutedEventArgs e) => await LoadVisibleSectionAsync();
    /// <summary>首次激活对应区域时独立读取，不因表格选择重复加载。</summary>
    private async void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, SectionsTabs) && IsLoaded) await LoadVisibleSectionAsync();
    }
    /// <summary>选择绑定完成后才加载，卸载或更换上下文时不再触发旧VM。</summary>
    private async Task LoadVisibleSectionAsync()
    {
        if (DataContext is not MaterialTrackingDetailViewModel vm) return;
        await Dispatcher.Yield(DispatcherPriority.DataBind);
        if (IsLoaded && ReferenceEquals(vm, DataContext)) await vm.LoadSelectedSectionCommand.ExecuteAsync(null);
    }
}

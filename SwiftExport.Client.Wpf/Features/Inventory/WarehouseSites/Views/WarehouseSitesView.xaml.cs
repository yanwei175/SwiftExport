using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExportClient.Features.Inventory.WarehouseSites.ViewModels;
using System.Windows.Controls;

namespace SwiftExportClient.Features.Inventory.WarehouseSites.Views;

/// <summary>承载虚拟仓管理页面，并把 DataGrid 选择事件转交给页面协调器。</summary>
public partial class WarehouseSitesView : UserControl
{
    /// <summary>创建虚拟仓管理页面。</summary>
    public WarehouseSitesView() => InitializeComponent();

    /// <summary>传递真实旧/新选择，业务查询与未保存保护仍由 ViewModel 负责。</summary>
    private async void WarehouseGrid_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not WarehouseSitesViewModel viewModel) return;
        var previous = e.RemovedItems.OfType<WarehouseSiteDto>().FirstOrDefault();
        var current = e.AddedItems.OfType<WarehouseSiteDto>().FirstOrDefault();
        await viewModel.HandleSelectionChangedAsync(previous, current);
    }
}

using System.Windows;
using System.Windows.Controls;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;

namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Views
{
    /// <summary>承载只读采购列表；Loaded 仅触发幂等初始化，窗格 Scope 拥有模型生命周期。</summary>
    public partial class PurchaseOrderListView : UserControl
    {
        private readonly PurchaseOrderListViewModel model;
        /// <summary>使用当前窗格注入的模型，不解析全局服务或读取 Excel。</summary>
        public PurchaseOrderListView(PurchaseOrderListViewModel model)
        { InitializeComponent(); this.model = model; DataContext = model; Loaded += OnLoaded; }
        private async void OnLoaded(object sender, RoutedEventArgs args) { await model.InitializeAsync(); }
    }
}

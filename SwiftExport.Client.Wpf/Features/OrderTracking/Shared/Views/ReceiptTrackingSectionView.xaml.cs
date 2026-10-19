using System.Windows.Controls;
namespace SwiftExportClient.Features.OrderTracking.Shared.Views;
/// <summary>复用成品需求和物料范围的采购整行进度及收货记录，不在视图中汇总数量。</summary>
public partial class ReceiptTrackingSectionView : UserControl
{
    /// <summary>初始化独立收货区域。</summary>
    public ReceiptTrackingSectionView() => InitializeComponent();
}

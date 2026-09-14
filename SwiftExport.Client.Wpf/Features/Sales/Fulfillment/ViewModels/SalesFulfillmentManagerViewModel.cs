using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Sales.Fulfillment.ViewModels;

/// <summary>
/// 保留销售履约 Manager 的导航与页面承载外壳；当前不加载任何履约业务数据。
/// </summary>
public sealed partial class SalesFulfillmentManagerViewModel : ViewModelBaseEx
{
    /// <summary>销售履约 Manager 的稳定路由键。</summary>
    public const string RouteKey = "sales-fulfillment-manager";

    /// <summary>初始化只读页面外壳。</summary>
    public SalesFulfillmentManagerViewModel()
    {
        Title = "销售履约查询";
        IsReadOnly = true;
    }
}

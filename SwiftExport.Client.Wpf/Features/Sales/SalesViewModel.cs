using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Sales;

public sealed class SalesViewModel : ViewModelBaseEx
{
    public SalesViewModel()
    {
        Title = "销售";
        Description = "销售工作区将承载销售订单、需求链路和相关单据入口。";
    }

    public string Description { get; }
}

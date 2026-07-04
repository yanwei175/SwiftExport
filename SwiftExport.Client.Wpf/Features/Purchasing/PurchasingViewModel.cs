using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Purchasing;

public sealed class PurchasingViewModel : ViewModelBaseEx
{
    public PurchasingViewModel()
    {
        Title = "采购";
        Description = "采购工作区将承载采购订单、入库和供应商协同相关页面。";
    }

    public string Description { get; }
}

using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping;

public sealed class ShippingViewModel : ViewModelBaseEx
{
    public ShippingViewModel()
    {
        Title = "出货";
        Description = "出货工作区将承载 Packing List、出库和装箱相关页面。";
    }

    public string Description { get; }
}

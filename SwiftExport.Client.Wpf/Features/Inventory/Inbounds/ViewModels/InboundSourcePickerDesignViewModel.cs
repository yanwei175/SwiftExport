using SwiftExportClient.Features.Inventory.Inbounds.Models;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>仅供 XAML 设计器展示样例，不注册到运行时，不请求真实来源。</summary>
public sealed partial class InboundSourcePickerDesignViewModel : InboundSourcePickerViewModel
{
    /// <summary>构建设计时完整和部分收货样例；图片为空时使用现有控件占位。</summary>
    public InboundSourcePickerDesignViewModel()
    {
        LoadItems([
            new InboundSourcePickerRowModel
            {
                IsChecked = true, SourceNo = "PO-202609-001", LineNumber = 1,
                BoNo = "BO-202609-018", Sku = "CS-001", LinkSku = "FG-100",
                Description = "客供配件 · 金属连接件", Unit = "PCS",
                SupplierName = "示例供应商 A", WarehouseName = "示例收货仓 B",
                OriginalQty = 120, RemainingReceivableQty = 80
            },
            new InboundSourcePickerRowModel
            {
                SourceNo = "PO-202609-001", LineNumber = 2, BoNo = "BO-202609-018",
                Sku = "CS-002", LinkSku = "FG-100", Description = "客供配件 · 包装附件",
                Unit = "SET", SupplierName = "示例供应商 A", WarehouseName = "示例收货仓 B",
                OriginalQty = 60, RemainingReceivableQty = 60
            }
        ]);
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExportClient.Features.Inventory.Inbounds.Models;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>入库管理设计时数据；只由 d:DataContext 创建，不注册到运行时，也不访问 Picker 或后端。</summary>
public sealed partial class InboundManagerDesignViewModel : ObservableObject
{
    /// <summary>设计时头行集合，复用真实编辑模型以展示实际列绑定。</summary>
    public BaseListModel<InboundHeaderModel, InboundHeaderDetailDto> Headers { get; } = new(_ => new InboundHeaderModel());
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Lines))]
    public partial InboundHeaderModel? SelectedItem { get; set; }
    [ObservableProperty]
    public partial InboundDocumentTypeEnum SelectedDocumentType { get; set; } = InboundDocumentTypeEnum.PurchaseReceipt;
    [ObservableProperty]
    public partial string Keyword { get; set; } = string.Empty;
    /// <summary>选中单据的示例明细。</summary>
    public BaseListModel<InboundLineModel, InboundLineDetailDto>? Lines => SelectedItem?.Lines;
    /// <summary>设计器始终展示可用布局。</summary>
    public bool IsNotBusy => true;
    /// <summary>设计样例的头部编辑状态。</summary>
    public bool HeaderReadOnly => false;
    /// <summary>设计样例的明细编辑状态。</summary>
    public bool LinesReadOnly => false;
    /// <summary>明确样例数据不是真实查询结果。</summary>
    public string PageStatus => "设计时示例 · 2 张入库单";
    /// <summary>设计器页脚说明。</summary>
    public string ResultSummary => "设计时数据，不会保存或影响库存。";
    /// <summary>设计器无校验失败提示。</summary>
    public string ValidationSummary => string.Empty;

    /// <summary>建立采购草稿和生产类型展示样例，不生成业务事实。</summary>
    public InboundManagerDesignViewModel()
    {
        Headers.LoadFromDtos([
            new InboundHeaderDetailDto { Uid = Guid.NewGuid(), DocumentNo = "IR-DEMO-001", WarehouseNameSnapshot = "示例收货仓 A", WarehouseCodeSnapshot = "WH-A", WarehouseSiteId = Guid.NewGuid(), ReceiptDate = new DateTime(2026, 9, 14), Remark = "采购收货示例" },
            new InboundHeaderDetailDto { Uid = Guid.NewGuid(), DocumentNo = "IR-DEMO-002", DocumentType = InboundDocumentTypeEnum.ProductionReceipt, WarehouseNameSnapshot = "示例收货仓 B", CreationMode = InboundCreationModeEnum.Automatic, ReceiptDate = new DateTime(2026, 9, 14), Remark = "仅展示生产类型，功能尚未开放" }
        ]);
        SelectedItem = Headers.Items[0];
        SelectedItem.Lines.LoadFromDtos([
            new InboundLineDetailDto { Uid = Guid.NewGuid(), InboundDocumentId = SelectedItem.Uid, LineNumber = 1, SourceNo = "PO-DEMO-001", BoNo = "BO-001", SkuSnapshot = "SKU-001", LinkSku = "MODEL-A", DescriptionSnapshot = "金属连接件", UnitSnapshot = "PCS", OriginalQty = 100, RemainingReceivableQty = 60, Quantity = 20 },
            new InboundLineDetailDto { Uid = Guid.NewGuid(), InboundDocumentId = SelectedItem.Uid, LineNumber = 2, SourceNo = "PO-DEMO-001", BoNo = "BO-001", SkuSnapshot = "SKU-002", LinkSku = "MODEL-B", DescriptionSnapshot = "包装附件", UnitSnapshot = "SET", OriginalQty = 50, RemainingReceivableQty = 50, Quantity = 10 }
        ]);
    }
}

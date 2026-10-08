using SwiftExportClient.Services.UI;

namespace SwiftExportClient.Features.Products.Bom.Models;

/// <summary>复制来源选择结果，只返回确认的产品定位信息，不传递弹窗或其 ViewModel。</summary>
public sealed record BomSourceSelectionResult(DialogResult Reason, BomProductItemModel? Product);

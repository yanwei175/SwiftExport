using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>来源弹窗上下文；仅传入单据类型，选源不接受仓库筛选。</summary>
public sealed class InboundSourcePickerRequest
{
    public InboundDocumentTypeEnum DocumentType { get; init; }
    /// <summary>仅用于提示排除当前已保存草稿。</summary>
    public Guid? CurrentInboundDocumentId { get; init; }
}

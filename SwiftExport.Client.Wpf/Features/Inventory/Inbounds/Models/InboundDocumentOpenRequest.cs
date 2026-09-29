using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExportClient.Features.Inventory.Inbounds.Models;

/// <summary>从上游追溯页按正式入库单身份定位管理文档。</summary>
public sealed class InboundDocumentOpenRequest
{
    public Guid InboundDocumentId { get; init; }
    public string DocumentNo { get; init; } = string.Empty;
    public InboundPostingStatusEnum PostingStatus { get; init; }
}

using SwiftExport.Contracts.DTOs.Inventory.Outbounds;

namespace SwiftExportClient.Features.Inventory.Outbounds.Services;

/// <summary>展示过账影响和阻断项，只返回用户是否确认，不执行写入。</summary>
public interface IOutboundPostDialogService
{
    /// <summary>存在阻断项时只允许关闭；确认不代表服务端额度已锁定。</summary>
    Task<bool> ConfirmAsync(OutboundPostPreviewDto preview, CancellationToken token);
}

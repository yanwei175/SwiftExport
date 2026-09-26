using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Outbounds;

/// <summary>维护人工库存出库草稿，不产生正式库存事实。</summary>
public interface IOutboundDraftService
{
    /// <summary>创建草稿并返回单号、身份及版本。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> CreateAsync(CreateOutboundDraftCommand command, CancellationToken token = default);
    /// <summary>按期望版本更新草稿。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateOutboundDraftCommand command, CancellationToken token = default);
    /// <summary>按期望版本删除未过账草稿。</summary>
    Task<ServiceResult<OutboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteOutboundDraftCommand command, CancellationToken token = default);
}

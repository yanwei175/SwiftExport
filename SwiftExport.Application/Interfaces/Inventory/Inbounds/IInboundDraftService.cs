using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Inbounds;

/// <summary>提供人工入库草稿创建、整单更新和软删除的命令用例。</summary>
public interface IInboundDraftService
{
    /// <summary>在同一事务中创建草稿、审计和可重放回执。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> CreateAsync(CreateInboundDraftCommand command, CancellationToken token = default);
    /// <summary>按整单版本更新完整草稿明细。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> UpdateAsync(Guid uid, UpdateInboundDraftCommand command, CancellationToken token = default);
    /// <summary>按版本软删除人工草稿并记录原因。</summary>
    Task<ServiceResult<InboundDraftCommandResult>> DeleteAsync(Guid uid, DeleteInboundDraftCommand command, CancellationToken token = default);
}

using Mapster;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Shipping;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>只读取原执行头行，保留拆箱及反向关联，不根据当前采购或库存重新推算。</summary>
public sealed class PackingListTraceService(IPackingListRepository packingLists,
    IPackingListExecutionRepository executions, ICurrentUser user, IUserPermissionService permissions,
    TypeAdapterConfig mapping) : IPackingListTraceService
{
    /// <inheritdoc/>
    public async Task<ServiceResult<PackingListTraceDto>> GetAsync(Guid uid, CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } actor
            || !await permissions.HasPermissionAsync(actor, PermissionKeys.ModuleShippingAccess, token))
            return ServiceResult<PackingListTraceDto>.Fail("FORBIDDEN", "没有出货模块访问权限。");
        if (uid == Guid.Empty) return ServiceResult<PackingListTraceDto>.Fail("INVALID_COMMAND", "箱单身份不能为空。");
        var detail = await packingLists.GetDetailAsync(uid, token);
        if (detail == null) return ServiceResult<PackingListTraceDto>.Fail("NOT_FOUND", "箱单不存在。");
        if (!await executions.IsSchemaReadyAsync(token))
            return ServiceResult<PackingListTraceDto>.Fail("SCHEMA_NOT_READY", "箱单执行记录表尚未部署。");
        var facts = await executions.ReadAsync(uid, token);
        // 历史损坏不得伪装成空执行；也不能把其他箱单的执行证据混入当前响应。
        var ids = facts.Heads.Select(x => x.Uid).ToHashSet();
        if (ids.Count != facts.Heads.Count || facts.Heads.Any(x => x.PackingListHeadId != uid || x.IsDeleted)
            || facts.Lines.Any(x => !ids.Contains(x.ExecutionId) || x.IsDeleted)
            || facts.Lines.Select(x => x.Uid).Distinct().Count() != facts.Lines.Count)
            return ServiceResult<PackingListTraceDto>.Fail("EXECUTION_INVALID", "执行记录归属或完整性异常。");
        var result = new PackingListTraceDto { PackingListId = uid, PackingListNo = detail.Header.PackingListNo,
            HasExecutionEvidence = facts.Heads.Count > 0,
            Notice = facts.Heads.Count == 0 ? "尚无可证明的执行记录；不能据此推算历史库存或反向。" : "数量方向由执行操作决定，正向与反向不能直接累加。" };
        foreach (var head in facts.Heads.OrderBy(x => x.CreatedDate).ThenBy(x => x.Operation).ThenBy(x => x.Uid))
        {
            var item = head.Adapt<PackingListExecutionTraceDto>(mapping);
            item.Lines = facts.Lines.Where(x => x.ExecutionId == head.Uid).OrderBy(x => x.LineNumber).ThenBy(x => x.Uid)
                .Select(x => x.Adapt<PackingListExecutionLineTraceDto>(mapping)).ToList();
            result.Executions.Add(item);
        }
        token.ThrowIfCancellationRequested();
        return ServiceResult<PackingListTraceDto>.Ok(result);
    }
}

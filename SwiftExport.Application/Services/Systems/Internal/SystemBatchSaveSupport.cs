using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces.Data;

namespace SwiftExport.Application.Services.Systems.Internal;

/// <summary>集中处理系统资料批量保存的输入、受保护字段和失败回滚辅助，不拥有业务事务。</summary>
internal static class SystemBatchSaveSupport
{
    /// <summary>校验批量请求结构、重复身份和版本；不读取数据库事实。</summary>
    public static string? ValidateRequest<TDto>(BatchSaveRequestBase<TDto>? request)
    {
        if (request is null) return "请求不能为空。";

        request.CreateItems ??= [];
        request.UpdateItems ??= [];
        request.DeleteItems ??= [];
        if (request.CreateItems.Any(item => item is null)
            || request.UpdateItems.Any(item => item is null)
            || request.DeleteItems.Any(item => item is null))
            return "批量保存明细不能包含空项。";
        if (!request.HasChanges) return "没有需要保存的数据。";

        var duplicateUpdate = FindDuplicateUid(request.UpdateItems.Select(item => item.Uid));
        if (duplicateUpdate.HasValue) return $"同一记录不能重复修改：{duplicateUpdate}。";
        var duplicateDelete = FindDuplicateUid(request.DeleteItems.Select(item => item.Uid));
        if (duplicateDelete.HasValue) return $"同一记录不能重复删除：{duplicateDelete}。";
        var conflict = request.UpdateItems.Select(item => item.Uid)
            .Intersect(request.DeleteItems.Select(item => item.Uid))
            .FirstOrDefault(uid => uid != Guid.Empty);
        if (conflict != Guid.Empty) return $"同一记录不能同时修改和删除：{conflict}。";

        var invalidUpdate = request.UpdateItems.FirstOrDefault(item => item.Uid == Guid.Empty || item.Version <= 0);
        if (invalidUpdate is not null) return $"第 {FormatRow(invalidUpdate.RowIndex)} 行：修改标识或版本无效。";
        var invalidDelete = request.DeleteItems.FirstOrDefault(item => item.Uid == Guid.Empty || item.Version <= 0);
        if (invalidDelete is not null) return $"第 {FormatRow(invalidDelete.RowIndex)} 行：删除标识或版本无效。";
        return null;
    }

    /// <summary>把仓储失败转换为批量命令中止，使调用方回滚整组写入。</summary>
    public static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success) throw new SystemBatchWriteException(result.Message ?? fallbackMessage);
    }

    /// <summary>从待删除项中识别服务端保护记录，供资源服务拒绝删除。</summary>
    public static Guid[] FindProtectedDeleteIds<TEntity>(
        IEnumerable<DeleteItemRequest> deleteItems,
        IReadOnlyDictionary<Guid, TEntity> existing,
        Func<TEntity, bool> isProtected) => deleteItems
        .Where(item => existing.TryGetValue(item.Uid, out var entity) && isProtected(entity))
        .Select(item => item.Uid)
        .ToArray();

    /// <summary>复制可编辑字段并保留实体身份、审计和版本等服务端字段。</summary>
    public static void AdaptPreservingServerFields<TDto, TEntity>(TDto dto, TEntity entity)
        where TEntity : BaseEntity
    {
        var uid = entity.Uid;
        var isDeleted = entity.IsDeleted;
        var createdDate = entity.CreatedDate;
        var createdBy = entity.CreatedBy;
        dto.Adapt(entity);
        entity.Uid = uid;
        entity.IsDeleted = isDeleted;
        entity.CreatedDate = createdDate;
        entity.CreatedBy = createdBy;
    }

    /// <summary>在调用方失败时尝试回滚既有事务，避免清理错误覆盖原始异常。</summary>
    public static void TryRollback(IDbSession session, ILogger logger, string operation)
    {
        try
        {
            session.Transaction?.Rollback();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "{Operation}事务回滚失败。", operation);
        }
    }

    /// <summary>格式化批量请求的行号用于字段级错误提示。</summary>
    public static string FormatRow(int? rowIndex) => rowIndex?.ToString() ?? "?";

    private static Guid? FindDuplicateUid(IEnumerable<Guid> ids) => ids
        .Where(uid => uid != Guid.Empty)
        .GroupBy(uid => uid)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;
}

/// <summary>标识系统资料批量写入中止，交由原事务所有者回滚。</summary>
internal sealed class SystemBatchWriteException(string message) : Exception(message);

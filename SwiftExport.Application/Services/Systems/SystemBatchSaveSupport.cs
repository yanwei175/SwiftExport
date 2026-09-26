using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Systems;

internal static class SystemBatchSaveSupport
{
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

    public static void EnsureWriteSucceeded(RepositoryWriteResult result, string fallbackMessage)
    {
        if (!result.Success) throw new SystemBatchWriteException(result.Message ?? fallbackMessage);
    }

    public static Guid[] FindProtectedDeleteIds<TEntity>(
        IEnumerable<DeleteItemRequest> deleteItems,
        IReadOnlyDictionary<Guid, TEntity> existing,
        Func<TEntity, bool> isProtected) => deleteItems
        .Where(item => existing.TryGetValue(item.Uid, out var entity) && isProtected(entity))
        .Select(item => item.Uid)
        .ToArray();

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

    public static void TryRollback(DbSession session, ILogger logger, string operation)
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

    public static string FormatRow(int? rowIndex) => rowIndex?.ToString() ?? "?";

    private static Guid? FindDuplicateUid(IEnumerable<Guid> ids) => ids
        .Where(uid => uid != Guid.Empty)
        .GroupBy(uid => uid)
        .FirstOrDefault(group => group.Count() > 1)
        ?.Key;
}

internal sealed class SystemBatchWriteException(string message) : Exception(message);

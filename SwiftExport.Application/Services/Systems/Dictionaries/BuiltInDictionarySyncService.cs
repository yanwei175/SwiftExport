using System.Data;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Systems.Dictionaries;
using SwiftExport.Application.Services.Systems.Internal;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Types;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces.Systems.Dictionaries.Types;

namespace SwiftExport.Application.Services.Systems.Dictionaries;

/// <summary>显式补齐代码目录中的类型和默认值；事务内保留旧身份、用户配置及目录外记录。</summary>
public sealed class BuiltInDictionarySyncService(
    ISysDictTypeRepository types, ISysDictDataRepository data,
    ICurrentUser currentUser, IPermissionEvaluationService permissions, ServiceBaseContext context)
    : IBuiltInDictionarySyncService
{
    private readonly ILogger _logger = context.LoggerFactory.CreateLogger<BuiltInDictionarySyncService>();

    /// <summary>先校验编辑权限，再串行补齐整个内置目录；已有配置不覆盖，失败或取消时回滚本次全部写入。</summary>
    public async Task<ServiceResult<BuiltInDictionarySyncResponse>> SyncAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var userId = currentUser.UserId ?? Guid.Empty;
        if (!currentUser.IsAuthenticated || userId == Guid.Empty)
            return ServiceResult<BuiltInDictionarySyncResponse>.Fail(ServiceErrorCodes.Unauthenticated, "请先登录后再同步字典。");
        if (!await permissions.HasPermissionAsync(userId, PermissionKeys.SystemDictionaryEdit, cancellationToken))
            return ServiceResult<BuiltInDictionarySyncResponse>.Fail(ServiceErrorCodes.Forbidden, "当前账号缺少字典编辑权限。");

        var session = context.Session;
        // 这是独立的管理操作，不能接管调用方事务，也不能提交调用方尚未完成的其他写入。
        if (session.Transaction is not null)
            return ServiceResult<BuiltInDictionarySyncResponse>.Fail(ServiceErrorCodes.BusinessBlocked, "当前事务尚未结束，请单独执行内置字典同步。");
        IDbTransaction? transaction = null;
        try
        {
            transaction = session.Connection.BeginTransaction(IsolationLevel.ReadCommitted);
            session.Transaction = transaction;
            // 同步之间先互斥，再遵循现有的“类型 → 明细、各表按 Uid”锁顺序；覆盖空库首次插入竞争。
            await types.LockBuiltInSyncAsync(cancellationToken);
            var storedTypes = await types.GetAllIncludingDeletedAsync(true, cancellationToken);
            var storedData = await data.GetForBuiltInMergeAsync(null, true, cancellationToken);
            IReadOnlyList<SysDictType> effectiveTypes;
            IReadOnlyList<SysDictData> effectiveData;
            try
            {
                // 复用查询的身份判定；两个目录都完成冲突检查后才允许产生任何写入。
                effectiveTypes = BuiltInDictionaryCatalog.MergeTypes(storedTypes);
                effectiveData = BuiltInDictionaryCatalog.MergeData(storedData);
            }
            catch (InvalidOperationException exception)
            {
                _logger.LogWarning(exception, "内置字典同步的身份或编码检查失败。");
                SystemBatchSaveSupport.TryRollback(session, _logger, "内置字典同步");
                return ServiceResult<BuiltInDictionarySyncResponse>.Fail(ServiceErrorCodes.Validation, "内置字典身份或编码存在冲突，请检查字典数据后重试。");
            }

            var result = new BuiltInDictionarySyncResponse();
            var typesById = storedTypes.ToDictionary(item => item.Uid);
            foreach (var effective in effectiveTypes.Where(item => BuiltInDictionaryCatalog.FindType(item.DictCode) is not null))
            {
                var exists = typesById.TryGetValue(effective.Uid, out var stored);
                if (exists && !stored!.IsDeleted && stored.IsSystem) continue;
                // 恢复旧记录时采用原字段，避免把用户名称、分组和层级替换成代码默认值。
                var entity = stored ?? effective;
                var write = await types.SaveBuiltInAsync(entity, exists ? entity.Version : 0, cancellationToken);
                if (!write.Success)
                {
                    SystemBatchSaveSupport.TryRollback(session, _logger, "内置字典同步");
                    return ServiceResult<BuiltInDictionarySyncResponse>.Fail(write.ErrorCode ?? ServiceErrorCodes.Unexpected, write.Message);
                }
                if (exists) result.UpdatedTypeCount++; else result.AddedTypeCount++;
            }

            var dataById = storedData.ToDictionary(item => item.Uid);
            foreach (var effective in effectiveData.Where(BuiltInDictionaryCatalog.IsBuiltIn))
            {
                var exists = dataById.TryGetValue(effective.Uid, out var stored);
                if (exists && !stored!.IsDeleted && stored.IsEnabled && stored.IsSystem) continue;
                // 显式同步恢复有效标记，但显示字段仍采用数据库原值；仅缺失项使用目录默认值。
                var entity = stored ?? effective;
                var write = await data.SaveBuiltInOverrideAsync(entity, exists ? entity.Version : 0, cancellationToken);
                if (!write.Success)
                {
                    SystemBatchSaveSupport.TryRollback(session, _logger, "内置字典同步");
                    return ServiceResult<BuiltInDictionarySyncResponse>.Fail(write.ErrorCode ?? ServiceErrorCodes.Unexpected, write.Message);
                }
                if (exists) result.UpdatedDataCount++; else result.AddedDataCount++;
            }

            // 无输入的补齐操作以稳定身份和数据库现状实现幂等；重复调用跳过未变化项，不递增版本或刷新审计。
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            _logger.LogInformation("内置字典同步完成：新增类型 {Types}，修复类型 {UpdatedTypes}，新增明细 {Data}，修复明细 {UpdatedData}。",
                result.AddedTypeCount, result.UpdatedTypeCount, result.AddedDataCount, result.UpdatedDataCount);
            return ServiceResult<BuiltInDictionarySyncResponse>.Ok(result, "内置字典同步完成。");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SystemBatchSaveSupport.TryRollback(session, _logger, "内置字典同步");
            throw;
        }
        catch (Exception exception)
        {
            SystemBatchSaveSupport.TryRollback(session, _logger, "内置字典同步");
            _logger.LogError(exception, "内置字典同步失败。");
            return ServiceResult<BuiltInDictionarySyncResponse>.Fail(ServiceErrorCodes.Unexpected, "内置字典同步失败，所有操作已回滚，请刷新后重试。");
        }
        finally
        {
            if (transaction is not null)
            {
                session.Transaction = null;
                try { transaction.Dispose(); }
                catch (Exception exception) { _logger.LogError(exception, "内置字典同步事务释放失败。"); }
            }
        }
    }
}

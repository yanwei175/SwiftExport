using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Systems.ExcelReadProfiles;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExcelReadProfiles;
using SwiftExport.Core.Interfaces.Systems.ExcelReadProfiles;

namespace SwiftExport.Application.Services.Systems.ExcelReadProfiles;

/// <summary>维护地址读取配置聚合；主版本覆盖全部字段，允许分步保存，不执行实际 Excel 读取。</summary>
public sealed class ExcelReadProfileService(IExcelReadProfileRepository repository, ServiceBaseContext context) : IExcelReadProfileService
{
    private readonly ILogger logger = context.LoggerFactory.CreateLogger<ExcelReadProfileService>();

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>> ListAsync(string groupCode, CancellationToken cancellationToken)
    {
        if (!ExcelReadProfilePolicy.IsGroupValid(groupCode))
            return Fail<IReadOnlyList<ExcelReadProfileListItemDto>>(ServiceErrorCodes.Validation, "单据分组格式无效。", "groupCode");
        var profiles = await repository.ListAsync(groupCode.Trim(), cancellationToken);
        return ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>.Ok(profiles.Select(head => new ExcelReadProfileListItemDto
        { Uid = head.Uid, Version = head.Version, GroupCode = head.GroupCode, ConfigName = head.ConfigName }).ToArray());
    }

    /// <inheritdoc />
    public Task<ServiceResult<ExcelReadProfileDto>> GetAsync(Guid uid, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty) return Task.FromResult(Invalid("配置标识无效。", "uid"));
        // 短读取事务保留主记录共享锁，防止返回旧主版本配上新字段的混合快照。
        return InTransaction(async () =>
        {
            var head = await repository.FindAsync(uid, false, cancellationToken);
            return head is null ? NotFound<ExcelReadProfileDto>()
                : ServiceResult<ExcelReadProfileDto>.Ok(ExcelReadProfileMapping.ToDto(head, await repository.GetFieldsAsync(uid, cancellationToken)));
        }, IsolationLevel.RepeatableRead, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<ExcelReadProfileDto>> CreateAsync(CreateExcelReadProfileRequest request, CancellationToken cancellationToken)
    {
        if (request is null || !ExcelReadProfilePolicy.IsGroupValid(request.GroupCode)) return Task.FromResult(Invalid("单据分组格式无效。", "GroupCode"));
        var error = ExcelReadProfilePolicy.ValidateName(request.ConfigName, request.Remark);
        if (error is not null) return Task.FromResult(Invalid(error.Message, error.Field));
        return InTransaction(async () =>
        {
            var head = new ExcelReadProfile { GroupCode = request.GroupCode.Trim(), ConfigName = request.ConfigName.Trim(), Remark = EmptyToNull(request.Remark) };
            await repository.InsertAsync(head, cancellationToken);
            return ServiceResult<ExcelReadProfileDto>.Ok(ExcelReadProfileMapping.ToDto(head, []));
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<ExcelReadProfileDto>> UpdateAsync(Guid uid, UpdateExcelReadProfileRequest request, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty || request is null) return Task.FromResult(Invalid("配置标识或请求无效。"));
        var error = ExcelReadProfilePolicy.ValidateUpdate(request);
        if (error is not null) return Task.FromResult(Invalid(error.Message, error.Field));
        return InTransaction(async () =>
        {
            var head = await repository.FindAsync(uid, true, cancellationToken);
            if (head is null) return NotFound<ExcelReadProfileDto>();
            if (head.Version != request.ExpectedVersion) return Conflict<ExcelReadProfileDto>();
            var current = await repository.GetFieldsAsync(uid, cancellationToken);
            head.ConfigName = request.ConfigName.Trim();
            head.Remark = EmptyToNull(request.Remark);
            head.EndMode = request.EndMode;
            head.LastRowColumn = EmptyToNull(request.LastRowColumn)?.ToUpperInvariant();
            head.SummaryCellAddress = ExcelReadProfilePolicy.TryCell(request.SummaryCellAddress, out var summary, out _) ? summary : null;
            if (!await repository.UpdateAsync(head, request.ExpectedVersion, cancellationToken)) return Conflict<ExcelReadProfileDto>();

            var keys = request.Fields.Select(field => field.TargetFieldKey.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var old in current.Where(field => !keys.Contains(field.TargetFieldKey)))
            {
                old.IsDeleted = true;
                await repository.SaveFieldAsync(old, false, cancellationToken);
            }
            var byKey = current.ToDictionary(field => field.TargetFieldKey, StringComparer.OrdinalIgnoreCase);
            foreach (var input in request.Fields)
            {
                ExcelReadProfilePolicy.TryCell(input.CellAddress, out var address, out _);
                // 空名称表示没有参考信息，不能据此改变前端活动工作表的读取目标。
                var worksheetName = string.IsNullOrWhiteSpace(input.WorksheetName) ? null : input.WorksheetName;
                var isNew = !byKey.TryGetValue(input.TargetFieldKey.Trim(), out var field);
                field ??= new ExcelReadField { ProfileUid = uid, TargetFieldKey = input.TargetFieldKey.Trim(), WorksheetName = worksheetName, CellAddress = address };
                if (!isNew && field.WorksheetName == worksheetName && field.CellAddress == address && field.IsDownward == input.IsDownward) continue;
                field.WorksheetName = worksheetName;
                field.CellAddress = address;
                field.IsDownward = input.IsDownward;
                await repository.SaveFieldAsync(field, isNew, cancellationToken);
            }
            return ServiceResult<ExcelReadProfileDto>.Ok(ExcelReadProfileMapping.ToDto(head, await repository.GetFieldsAsync(uid, cancellationToken)));
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    /// <inheritdoc />
    public Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty || expectedVersion <= 0) return Task.FromResult(Fail<bool>(ServiceErrorCodes.Validation, "配置标识或预期版本无效。"));
        return InTransaction(async () =>
        {
            var head = await repository.FindAsync(uid, true, cancellationToken);
            if (head is null) return NotFound<bool>();
            if (head.Version != expectedVersion) return Conflict<bool>();
            head.IsDeleted = true;
            if (!await repository.UpdateAsync(head, expectedVersion, cancellationToken)) return Conflict<bool>();
            foreach (var field in await repository.GetFieldsAsync(uid, cancellationToken))
            {
                field.IsDeleted = true;
                await repository.SaveFieldAsync(field, false, cancellationToken);
            }
            return ServiceResult<bool>.Ok(true);
        }, IsolationLevel.ReadCommitted, cancellationToken);
    }

    /// <summary>只拥有本层创建的短事务；失败或取消回滚，清理异常不覆盖原始失败。</summary>
    private async Task<ServiceResult<T>> InTransaction<T>(Func<Task<ServiceResult<T>>> action, IsolationLevel isolation, CancellationToken cancellationToken)
    {
        var session = context.Session;
        if (session.Transaction is not null) return Fail<T>(ServiceErrorCodes.Unexpected, "当前请求已有未完成事务。");
        cancellationToken.ThrowIfCancellationRequested();
        var transaction = session.Connection.BeginTransaction(isolation);
        session.Transaction = transaction;
        try
        {
            var result = await action();
            cancellationToken.ThrowIfCancellationRequested();
            if (result.Success) transaction.Commit();
            else Rollback(transaction);
            return result;
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            // SQL Server 取消可能表现为 SqlException；仍以请求取消透传，不能伪装成普通保存失败。
            Rollback(transaction);
            throw new OperationCanceledException(cancellationToken);
        }
        catch (SqlException exception) when (exception.Number is 2601 or 2627)
        {
            Rollback(transaction);
            return Fail<T>("EXCEL_READ_DUPLICATE_NAME", "同一单据分组的配置名称已存在。", "ConfigName");
        }
        catch (OperationCanceledException) { Rollback(transaction); throw; }
        catch (Exception exception)
        {
            Rollback(transaction);
            logger.LogError(exception, "Excel 地址读取配置事务失败");
            return Fail<T>(ServiceErrorCodes.Unexpected, "配置操作失败，改动已回滚。");
        }
        finally
        {
            session.Transaction = null;
            try { transaction.Dispose(); }
            catch (Exception exception) { logger.LogError(exception, "Excel 地址读取配置事务释放失败"); }
        }
    }

    /// <summary>尽力回滚并记录清理错误，避免掩盖原始业务错误或请求取消。</summary>
    private void Rollback(IDbTransaction transaction)
    {
        try { transaction.Rollback(); }
        catch (Exception exception) { logger.LogError(exception, "Excel 地址读取配置事务回滚失败"); }
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static ServiceResult<ExcelReadProfileDto> Invalid(string message, string? field = null) => Fail<ExcelReadProfileDto>(ServiceErrorCodes.Validation, message, field);
    private static ServiceResult<T> Fail<T>(string code, string message, string? field = null) => ServiceResult<T>.Fail(code, message, field);
    private static ServiceResult<T> NotFound<T>() => Fail<T>(ServiceErrorCodes.NotFound, "配置不存在或已删除。");
    private static ServiceResult<T> Conflict<T>() => Fail<T>(ServiceErrorCodes.VersionConflict, "配置已由其他人修改，请重新加载后再保存。");
}

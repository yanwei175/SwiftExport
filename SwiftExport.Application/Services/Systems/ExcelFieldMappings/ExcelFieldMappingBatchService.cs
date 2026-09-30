using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Interfaces.Systems.ExcelFieldMappings;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExcelFieldMappings;
using SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Systems.ExcelFieldMappings;

/// <summary>锁定一个业务分组，校验完整提交快照，并原子新增或更新映射。</summary>
public sealed class ExcelFieldMappingBatchService(
    IExcelFieldMappingRepository repository, DbSession session, ILogger<ExcelFieldMappingBatchService> logger)
    : IExcelFieldMappingBatchService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> SaveGroupAsync(
        string groupCode, SaveExcelFieldMappingGroupRequest request, CancellationToken cancellationToken)
    {
        var group = ExcelFieldCatalog.ResolveGroup(groupCode);
        if (group is null || request?.Rows is null)
            return Fail(ServiceErrorCodes.Validation, "分组或保存内容无效。");
        if (request.Rows.Any(row => row is null) || request.Rows.Where(row => row.Uid != Guid.Empty)
                .GroupBy(row => row.Uid).Any(items => items.Count() > 1))
            return Fail(ServiceErrorCodes.Validation, "保存内容包含空行或重复的记录标识。");
        if (session.Transaction is not null)
            return Fail(ServiceErrorCodes.Unexpected, "当前请求已有未完成事务。");

        try
        {
            session.Transaction = session.Connection.BeginTransaction(IsolationLevel.Serializable);
            var current = await repository.GetByGroupForUpdateAsync(group, cancellationToken);
            var byId = current.ToDictionary(row => row.Uid);
            var submitted = new List<(SaveExcelFieldMappingRowRequest Input, ExcelFieldMapping? Current, string Header, string Normalized, string? Remark)>();
            var seenHeaders = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in request.Rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var old = row.Uid == Guid.Empty ? null : byId.GetValueOrDefault(row.Uid);
                if (row.Uid != Guid.Empty && old is null)
                    throw new SaveFailure(ServiceErrorCodes.NotFound, "映射不存在或不属于当前分组，请刷新后重试。");
                if ((old is null && row.ExpectedVersion != 0) || (old is not null && old.Version != row.ExpectedVersion))
                    throw new SaveFailure(ServiceErrorCodes.VersionConflict, "字段映射已被其他人修改，请刷新后重试。");
                var header = row.ExcelHeader?.Trim() ?? string.Empty;
                var normalized = ExcelHeaderNormalizer.Normalize(header);
                var field = row.TargetFieldKey?.Trim();
                var remark = row.Remark?.Trim();
                if (header.Length > 200 || normalized.Length > 200 || remark?.Length > 500
                    || string.IsNullOrWhiteSpace(field) || !ExcelFieldCatalog.ContainsField(group, field))
                    throw new SaveFailure(ServiceErrorCodes.Validation, "表头、目标字段或备注无效。");
                if (normalized.Length == 0 && (old is null || row.IsEnabled))
                    throw new SaveFailure(ServiceErrorCodes.Validation, "新映射必须填写表头；空表头种子行必须保持停用。");
                if (normalized.Length > 0 && !seenHeaders.Add(normalized))
                    throw new SaveFailure(ExcelFieldMappingErrorCodes.DuplicateHeader, "当前分组中有重复的 Excel 表头。");
                submitted.Add((row, old, header, normalized, remark));
            }

            var submittedIds = submitted.Where(item => item.Current is not null).Select(item => item.Current!.Uid).ToHashSet();
            if (current.Any(row => !submittedIds.Contains(row.Uid)
                && row.NormalizedHeader.Length > 0 && seenHeaders.Contains(row.NormalizedHeader)))
                throw new SaveFailure(ExcelFieldMappingErrorCodes.DuplicateHeader, "Excel 表头与未提交的现有映射冲突。");

            var changed = submitted.Where(item => item.Current is not null && IsChanged(item)).ToArray();
            var release = changed.Where(item => item.Current!.NormalizedHeader != item.Normalized)
                .Select(item => item.Current!.Uid).ToArray();
            if (!await repository.ReleaseHeadersAsync(release, cancellationToken))
                throw new SaveFailure(ServiceErrorCodes.VersionConflict, "字段映射已被其他人修改，请刷新后重试。");

            foreach (var item in submitted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ExcelFieldMapping entity;
                if (item.Current is null)
                    entity = new ExcelFieldMapping { GroupCode = group, ExcelHeader = item.Header,
                        NormalizedHeader = item.Normalized, TargetFieldKey = item.Input.TargetFieldKey.Trim(),
                        IsEnabled = item.Input.IsEnabled, Remark = item.Remark };
                else
                {
                    if (!IsChanged(item)) continue;
                    entity = item.Current;
                    entity.ExcelHeader = item.Header;
                    entity.NormalizedHeader = item.Normalized;
                    entity.TargetFieldKey = item.Input.TargetFieldKey.Trim();
                    entity.IsEnabled = item.Input.IsEnabled;
                    entity.Remark = item.Remark;
                }
                var written = item.Current is null ? await repository.AddAsync(entity) : await repository.UpdateAsync(entity);
                if (!written.Success)
                    throw new SaveFailure(ServiceErrorCodes.VersionConflict, "字段映射保存失败，所有改动已回滚，请刷新后重试。");
            }
            var saved = await repository.GetByGroupAsync(group, true, cancellationToken);
            session.Transaction.Commit();
            return ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Ok(saved.Select(row => new ExcelFieldMappingDto
            {
                Uid = row.Uid, Version = row.Version, GroupCode = row.GroupCode,
                ExcelHeader = row.ExcelHeader, TargetFieldKey = row.TargetFieldKey,
                IsEnabled = row.IsEnabled, Remark = row.Remark
            }).ToArray());
        }
        catch (SaveFailure error)
        {
            Rollback();
            return Fail(error.Code, error.Message);
        }
        catch (SqlException error) when (error.Number is 2601 or 2627)
        {
            Rollback();
            return Fail(ExcelFieldMappingErrorCodes.DuplicateHeader, "该分组中的 Excel 表头已存在。");
        }
        catch (OperationCanceledException)
        {
            Rollback();
            throw;
        }
        catch (Exception error)
        {
            Rollback();
            logger.LogError(error, "Excel 字段映射整组保存失败");
            return Fail(ServiceErrorCodes.Unexpected, "字段映射保存失败，所有改动已回滚。");
        }
        finally
        {
            session.Transaction?.Dispose();
            session.Transaction = null;
        }
    }

    // 仅比较客户端允许维护的字段，防止无改动的整组提交抬高全部版本。
    private static bool IsChanged((SaveExcelFieldMappingRowRequest Input, ExcelFieldMapping? Current, string Header, string Normalized, string? Remark) row)
        => row.Current is not null && (row.Current.ExcelHeader != row.Header
            || row.Current.NormalizedHeader != row.Normalized
            || row.Current.TargetFieldKey != row.Input.TargetFieldKey.Trim()
            || row.Current.IsEnabled != row.Input.IsEnabled || row.Current.Remark != row.Remark);

    private void Rollback()
    {
        try { session.Transaction?.Rollback(); }
        catch (Exception error) { logger.LogError(error, "Excel 字段映射事务回滚失败"); }
    }

    private static ServiceResult<IReadOnlyList<ExcelFieldMappingDto>> Fail(string code, string message)
        => ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Fail(code, message);

    private sealed class SaveFailure(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }
}

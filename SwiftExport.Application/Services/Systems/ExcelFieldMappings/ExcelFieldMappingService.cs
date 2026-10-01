// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using Microsoft.Data.SqlClient;
using SwiftExport.Application.Interfaces.Systems.ExcelFieldMappings;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.ExcelFieldMappings;
using SwiftExport.Core.Interfaces.Systems.ExcelFieldMappings;

namespace SwiftExport.Application.Services.Systems.ExcelFieldMappings;

/// <summary>验证配置格式、别名唯一性和预期版本，再委托仓储持久化。</summary>
public sealed class ExcelFieldMappingService(IExcelFieldMappingRepository repository) : IExcelFieldMappingService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken)
    {
        var group = ExcelFieldMappingFormat.NormalizeGroup(groupCode);
        if (group is null) return ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Fail(ServiceErrorCodes.Validation, "分组必须是 1 至 40 个字符的 ASCII 标识。", nameof(groupCode));
        var rows = await repository.GetByGroupAsync(group, includeDisabled, cancellationToken);
        return ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>.Ok(rows.Select(ToDto).ToArray());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync(CreateExcelFieldMappingRequest request, CancellationToken cancellationToken)
    {
        if (request is null) return Invalid("请求不能为空。");
        var group = ExcelFieldMappingFormat.NormalizeGroup(request.GroupCode);
        var validation = Validate(group, request.ExcelHeader, request.TargetFieldKey, request.Remark);
        if (validation is not null) return validation;
        var header = request.ExcelHeader.Trim();
        var normalized = ExcelHeaderNormalizer.Normalize(header);
        if (await repository.GetByNormalizedHeaderAsync(group!, normalized, cancellationToken) is not null) return Duplicate();

        var entity = new ExcelFieldMapping
        {
            GroupCode = group!, ExcelHeader = header, NormalizedHeader = normalized,
            TargetFieldKey = request.TargetFieldKey.Trim(), IsEnabled = request.IsEnabled,
            Remark = request.Remark?.Trim()
        };
        try
        {
            var written = await repository.AddAsync(entity);
            return written.Success ? ServiceResult<ExcelFieldMappingDto>.Ok(ToDto(entity))
                : ServiceResult<ExcelFieldMappingDto>.Fail(ServiceErrorCodes.Unexpected, "创建字段映射失败。");
        }
        catch (SqlException exception) when (IsUniqueConflict(exception)) { return Duplicate(); }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty || request is null) return Invalid("请求或映射标识无效。");
        if (request.ExpectedVersion <= 0) return Invalid("预期版本必须大于零。", nameof(request.ExpectedVersion));
        var entity = await repository.GetByIdAsync(uid);
        if (entity is null) return ServiceResult<ExcelFieldMappingDto>.Fail(ServiceErrorCodes.NotFound, "字段映射不存在。");
        if (entity.Version != request.ExpectedVersion) return Conflict();
        var validation = Validate(entity.GroupCode, request.ExcelHeader, request.TargetFieldKey, request.Remark);
        if (validation is not null) return validation;
        var normalized = ExcelHeaderNormalizer.Normalize(request.ExcelHeader);
        var duplicate = await repository.GetByNormalizedHeaderAsync(entity.GroupCode, normalized, cancellationToken);
        if (duplicate is not null && duplicate.Uid != uid) return Duplicate();

        entity.ExcelHeader = request.ExcelHeader.Trim();
        entity.NormalizedHeader = normalized;
        entity.TargetFieldKey = request.TargetFieldKey.Trim();
        entity.IsEnabled = request.IsEnabled;
        entity.Remark = request.Remark?.Trim();
        try
        {
            var written = await repository.UpdateAsync(entity);
            if (!written.Success) return Conflict();
            entity.Version = written.NewVersion ?? request.ExpectedVersion + 1;
            return ServiceResult<ExcelFieldMappingDto>.Ok(ToDto(entity));
        }
        catch (SqlException exception) when (IsUniqueConflict(exception)) { return Duplicate(); }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken)
    {
        if (uid == Guid.Empty || expectedVersion <= 0)
            return ServiceResult<bool>.Fail(ServiceErrorCodes.Validation, "映射标识或预期版本无效。");
        var entity = await repository.GetByIdAsync(uid);
        if (entity is null) return ServiceResult<bool>.Fail(ServiceErrorCodes.NotFound, "字段映射不存在。");
        if (entity.Version != expectedVersion) return ServiceResult<bool>.Fail(ServiceErrorCodes.VersionConflict, "字段映射已由其他人修改，请刷新后重试。");
        var written = await repository.DeleteAsync(uid, expectedVersion);
        return written.Success ? ServiceResult<bool>.Ok(true)
            : ServiceResult<bool>.Fail(ServiceErrorCodes.VersionConflict, "字段映射已由其他人修改，请刷新后重试。");
    }

    // 统一校验原始长度和规范化长度，避免 SQL 列截断；不承担模型字段或订单业务校验。
    private static ServiceResult<ExcelFieldMappingDto>? Validate(string? group, string? header, string? field, string? remark)
    {
        if (ExcelFieldMappingFormat.NormalizeGroup(group) is null) return Invalid("分组必须是 1 至 40 个字符的 ASCII 标识。", "GroupCode");
        if (string.IsNullOrWhiteSpace(header)) return Invalid("Excel 表头不能为空。", "ExcelHeader");
        if (header.Trim().Length > 200 || ExcelHeaderNormalizer.Normalize(header).Length > 200)
            return Invalid("Excel 表头不能超过 200 个字符。", "ExcelHeader");
        if (string.IsNullOrWhiteSpace(field) || !ExcelFieldMappingFormat.IsValidField(field.Trim()))
            return Invalid("目标字段必须是最多 100 个字符的点分隔 ASCII 标识。", "TargetFieldKey");
        if (remark?.Length > 500) return Invalid("备注不能超过 500 个字符。", "Remark");
        return null;
    }

    private static bool IsUniqueConflict(SqlException exception) => exception.Number is 2601 or 2627;
    private static ServiceResult<ExcelFieldMappingDto> Invalid(string message, string? field = null) => ServiceResult<ExcelFieldMappingDto>.Fail(ServiceErrorCodes.Validation, message, field);
    private static ServiceResult<ExcelFieldMappingDto> Duplicate() => ServiceResult<ExcelFieldMappingDto>.Fail(ExcelFieldMappingErrorCodes.DuplicateHeader, "该分组中的 Excel 表头已存在。", "ExcelHeader");
    private static ServiceResult<ExcelFieldMappingDto> Conflict() => ServiceResult<ExcelFieldMappingDto>.Fail(ServiceErrorCodes.VersionConflict, "字段映射已由其他人修改，请刷新后重试。");
    private static ExcelFieldMappingDto ToDto(ExcelFieldMapping entity) => new()
    {
        Uid = entity.Uid, Version = entity.Version, GroupCode = entity.GroupCode,
        ExcelHeader = entity.ExcelHeader, TargetFieldKey = entity.TargetFieldKey,
        IsEnabled = entity.IsEnabled, Remark = entity.Remark
    };
}

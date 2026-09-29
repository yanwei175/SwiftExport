using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.ExcelFieldMappings;

/// <summary>提供按组字段目录及映射配置的版本化维护操作。</summary>
public interface IExcelFieldMappingService
{
    /// <summary>列出可配置业务分组。</summary>
    ServiceResult<IReadOnlyList<ExcelFieldGroupDto>> GetGroups();
    /// <summary>列出分组允许映射的目标字段。</summary>
    ServiceResult<IReadOnlyList<ExcelTargetFieldDto>> GetFields(string groupCode);
    /// <summary>按分组读取配置，默认排除停用项。</summary>
    Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken);
    /// <summary>创建一条表头别名配置。</summary>
    Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync(CreateExcelFieldMappingRequest request, CancellationToken cancellationToken);
    /// <summary>以预期版本更新一条配置。</summary>
    Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken);
    /// <summary>以预期版本软删除一条配置。</summary>
    Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken);
}

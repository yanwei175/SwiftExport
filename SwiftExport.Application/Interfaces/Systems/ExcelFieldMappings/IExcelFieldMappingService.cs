// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.ExcelFieldMappings;

/// <summary>提供按组映射配置的版本化维护操作；可导入字段由客户端模型声明。</summary>
public interface IExcelFieldMappingService
{
    /// <summary>按分组读取配置，默认排除停用项。</summary>
    Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, bool includeDisabled, CancellationToken cancellationToken);
    /// <summary>创建一条表头别名配置。</summary>
    Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync(CreateExcelFieldMappingRequest request, CancellationToken cancellationToken);
    /// <summary>以预期版本更新一条配置。</summary>
    Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken);
    /// <summary>以预期版本软删除一条配置。</summary>
    Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken);
}

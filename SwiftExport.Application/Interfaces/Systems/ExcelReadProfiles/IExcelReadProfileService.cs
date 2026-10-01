using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.ExcelReadProfiles;

/// <summary>维护共享地址配置，允许结构未完成但格式合法的分步保存，返回完整性提示。</summary>
public interface IExcelReadProfileService
{
    /// <summary>读取同分组配置摘要。</summary>
    Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>> ListAsync(string groupCode, CancellationToken cancellationToken);
    /// <summary>读取一致的头行快照和完整性提示。</summary>
    Task<ServiceResult<ExcelReadProfileDto>> GetAsync(Guid uid, CancellationToken cancellationToken);
    /// <summary>创建空配置。</summary>
    Task<ServiceResult<ExcelReadProfileDto>> CreateAsync(CreateExcelReadProfileRequest request, CancellationToken cancellationToken);
    /// <summary>按主版本原子保存完整快照，缺席字段表示清除。</summary>
    Task<ServiceResult<ExcelReadProfileDto>> UpdateAsync(Guid uid, UpdateExcelReadProfileRequest request, CancellationToken cancellationToken);
    /// <summary>按主版本原子软删除配置及字段。</summary>
    Task<ServiceResult<bool>> DeleteAsync(Guid uid, int expectedVersion, CancellationToken cancellationToken);
}


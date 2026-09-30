using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.ExcelFieldMappings;

/// <summary>在一个数据库事务中保存字段映射分组的提交行。</summary>
public interface IExcelFieldMappingBatchService
{
    /// <summary>新增或更新提交行，缺席行不删除，并返回该组最新快照。</summary>
    Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> SaveGroupAsync(string groupCode, SaveExcelFieldMappingGroupRequest request, CancellationToken cancellationToken);
}

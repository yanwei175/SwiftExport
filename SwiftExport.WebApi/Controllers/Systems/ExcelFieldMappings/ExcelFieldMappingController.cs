using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Systems.ExcelFieldMappings;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Systems.ExcelFieldMappings;

/// <summary>提供字段映射配置的 HTTP 边界；身份与维护权限由全局过滤器检查。</summary>
[ApiController]
[Route("api/excel-field-mappings")]
public sealed class ExcelFieldMappingController(IExcelFieldMappingService service, IExcelFieldMappingBatchService batchService) : ControllerBase
{
    /// <summary>读取业务分组目录。</summary>
    [HttpGet("groups")]
    public ServiceResult<IReadOnlyList<ExcelFieldGroupDto>> GetGroupsAsync() => service.GetGroups();

    /// <summary>读取指定分组的可映射目标字段。</summary>
    [HttpGet("{groupCode}/fields")]
    public ServiceResult<IReadOnlyList<ExcelTargetFieldDto>> GetFieldsAsync(string groupCode) => service.GetFields(groupCode);

    /// <summary>读取指定分组的映射；维护页面可请求包含停用项。</summary>
    [HttpGet("{groupCode}")]
    public Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> GetMappingsAsync(string groupCode, [FromQuery] bool includeDisabled = false, CancellationToken cancellationToken = default)
        => service.GetMappingsAsync(groupCode, includeDisabled, cancellationToken);

    /// <summary>创建一条映射配置。</summary>
    [HttpPost]
    public Task<ServiceResult<ExcelFieldMappingDto>> CreateAsync([FromBody] CreateExcelFieldMappingRequest request, CancellationToken cancellationToken)
        => service.CreateAsync(request, cancellationToken);

    /// <summary>按预期版本更新配置。</summary>
    [HttpPut("{uid:guid}")]
    public Task<ServiceResult<ExcelFieldMappingDto>> UpdateAsync(Guid uid, [FromBody] UpdateExcelFieldMappingRequest request, CancellationToken cancellationToken)
        => service.UpdateAsync(uid, request, cancellationToken);

    /// <summary>整组原子保存映射，不把缺席行解释为删除。</summary>
    [HttpPut("{groupCode}/batch")]
    public Task<ServiceResult<IReadOnlyList<ExcelFieldMappingDto>>> SaveGroupAsync(string groupCode, [FromBody] SaveExcelFieldMappingGroupRequest request, CancellationToken cancellationToken)
        => batchService.SaveGroupAsync(groupCode, request, cancellationToken);

    /// <summary>按预期版本软删除配置。</summary>
    [HttpDelete("{uid:guid}")]
    public Task<ServiceResult<bool>> DeleteAsync(Guid uid, [FromQuery] int expectedVersion, CancellationToken cancellationToken)
        => service.DeleteAsync(uid, expectedVersion, cancellationToken);
}

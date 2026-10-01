using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.Systems.ExcelReadProfiles;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Systems.ExcelReadProfiles;

/// <summary>共享 Excel 地址配置的 HTTP 边界；维护权限由统一端点过滤器检查，不接收实际工作簿数据。</summary>
[Authorize]
[ApiController]
[Route("api/excel-read-profiles")]
public sealed class ExcelReadProfileController(IExcelReadProfileService service) : ControllerBase
{
    /// <summary>按单据分组读取配置选择列表。</summary>
    [HttpGet]
    public Task<ServiceResult<IReadOnlyList<ExcelReadProfileListItemDto>>> ListAsync([FromQuery] string groupCode, CancellationToken cancellationToken)
        => service.ListAsync(groupCode, cancellationToken);

    /// <summary>读取完整配置及结构完整性提示。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<ExcelReadProfileDto>> GetAsync(Guid uid, CancellationToken cancellationToken)
        => service.GetAsync(uid, cancellationToken);

    /// <summary>创建空配置，允许后续逐个指定字段。</summary>
    [HttpPost]
    public Task<ServiceResult<ExcelReadProfileDto>> CreateAsync([FromBody] CreateExcelReadProfileRequest request, CancellationToken cancellationToken)
        => service.CreateAsync(request, cancellationToken);

    /// <summary>以主版本原子更新完整快照；清除单字段时从 Fields 移除再提交。</summary>
    [HttpPut("{uid:guid}")]
    public Task<ServiceResult<ExcelReadProfileDto>> UpdateAsync(Guid uid, [FromBody] UpdateExcelReadProfileRequest request, CancellationToken cancellationToken)
        => service.UpdateAsync(uid, request, cancellationToken);

    /// <summary>按主版本软删除配置及全部字段。</summary>
    [HttpDelete("{uid:guid}")]
    public Task<ServiceResult<bool>> DeleteAsync(Guid uid, [FromQuery] int expectedVersion, CancellationToken cancellationToken)
        => service.DeleteAsync(uid, expectedVersion, cancellationToken);
}

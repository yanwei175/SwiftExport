using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.QualityControl.AfterSales.Query;

/// <summary>保持售后列表、详情和候选的原只读路由。</summary>
[ApiController, Authorize, Route("api/quality-control/after-sales")]
public sealed class AfterSalesQueryController(IAfterSalesQueryService service) : ControllerBase
{
    /// <summary>查询售后分页。</summary>
    [HttpGet]
    public Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync([FromQuery] AfterSalesSearchRequest request, CancellationToken token) => service.SearchAsync(request, token);

    /// <summary>读取头行一致性详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken token) => service.GetAsync(uid, token);

    /// <summary>查询白名单候选。</summary>
    [HttpGet("lookups/{kind}")]
    public Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, [FromQuery] AfterSalesLookupRequest request, CancellationToken token) => service.LookupAsync(kind, request, token);
}

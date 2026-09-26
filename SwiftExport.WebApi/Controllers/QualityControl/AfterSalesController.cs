using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces.QualityControl;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.QualityControl;
/// <summary>售后HTTP边界，只绑定参数并调用业务服务。</summary>
[ApiController,Authorize,Route("api/quality-control/after-sales")]
public sealed class AfterSalesController(IAfterSalesService service) : ControllerBase
{
    /// <summary>查询分页。</summary>
    [HttpGet]
    public Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync([FromQuery] AfterSalesSearchRequest request,CancellationToken token)=>service.SearchAsync(request,token);
    /// <summary>读取详情。</summary>
    [HttpGet("{uid:guid}")]
    public Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid,CancellationToken token)=>service.GetAsync(uid,token);
    /// <summary>查询候选。</summary>
    [HttpGet("lookups/{kind}")]
    public Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind,[FromQuery] AfterSalesLookupRequest request,CancellationToken token)=>service.LookupAsync(kind,request,token);
    /// <summary>创建售后事实。</summary>
    [HttpPost,PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> CreateAsync([FromBody] SaveAfterSalesCommand command,CancellationToken token)=>service.SaveAsync(command.Uid,command,true,token);
    /// <summary>保存售后事实。</summary>
    [HttpPut("{uid:guid}"),PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> SaveAsync(Guid uid,[FromBody] SaveAfterSalesCommand command,CancellationToken token)=>service.SaveAsync(uid,command,false,token);
    /// <summary>执行售后ConfirmDemand命令。</summary>
    [HttpPost("{uid:guid}/confirm-demand"),PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> ConfirmDemandAsync(Guid uid,[FromBody] ConfirmAfterSalesDemandCommand command,CancellationToken token)=>service.ConfirmDemandAsync(uid,command,token);
    /// <summary>执行售后CancelDemandConfirmation命令。</summary>
    [HttpPost("{uid:guid}/cancel-demand-confirmation"),PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> CancelDemandConfirmationAsync(Guid uid,[FromBody] CancelAfterSalesDemandConfirmationCommand command,CancellationToken token)=>service.CancelDemandConfirmationAsync(uid,command,token);
    /// <summary>执行售后Close命令。</summary>
    [HttpPost("{uid:guid}/close"),PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> CloseAsync(Guid uid,[FromBody] CloseAfterSalesCommand command,CancellationToken token)=>service.CloseAsync(uid,command,token);
    /// <summary>执行售后Reopen命令。</summary>
    [HttpPost("{uid:guid}/reopen"),PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> ReopenAsync(Guid uid,[FromBody] ReopenAfterSalesCommand command,CancellationToken token)=>service.ReopenAsync(uid,command,token);
    /// <summary>执行售后Void命令。</summary>
    [HttpPost("{uid:guid}/void"),PreventDuplicateRequest]
    public Task<ServiceResult<AfterSalesCommandResult>> VoidAsync(Guid uid,[FromBody] VoidAfterSalesCommand command,CancellationToken token)=>service.VoidAsync(uid,command,token);
}


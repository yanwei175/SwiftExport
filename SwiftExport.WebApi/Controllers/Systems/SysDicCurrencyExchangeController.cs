using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Systems;


public class SysDicCurrencyExchangeController(ISysDicCurrencyExchangeService service) : BaseController<SysDicCurrencyExchange, SysDicCurrencyExchangeDto>(service)
{


    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAll() => await service.GetAllAsync();

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SysDicCurrencyExchangeDto>>> GetPaged([FromBody] SysDicCurrencyExchangeSearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SysDicCurrencyExchangeBatchSaveResponse>> BatchSave([FromBody] SysDicCurrencyExchangeBatchSaveRequest req) => await service.BatchSaveAsync(req);
}

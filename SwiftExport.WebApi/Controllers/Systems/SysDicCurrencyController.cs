using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Systems;

public class SysDicCurrencyController(ISysDicCurrencyService service) : BaseController<SysDicCurrency, SysDicCurrencyDto>(service)
{
    // 向下转型，方便调用扩展方法
    private ISysDicCurrencyService CurrencyService => service;

    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }

    /// <summary>获取当前本位币</summary>
    [HttpGet("base")]
    public async Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrency()
    {
        return await CurrencyService.GetBaseCurrencyAsync();
    }

    /// <summary>获取所有启用的货币（用于下拉选择器）</summary>
    [HttpGet("enabled")]
    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledList()
    {
        return await CurrencyService.GetEnabledListAsync();
    }

    [HttpPost("setnewbase")]
    public async Task<ServiceResult<int>> SetNewBaseCurrency([FromBody] SysDicCurrencyDto dto)
    {
        return await service.SetNewBaseCurrencyAsync(dto);
    }


    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SysDicCurrencyDto>>> GetPaged([FromBody] SysDicCurrencySearchRequest request) => await service.GetPagedAsync(request);


    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SysDicCurrencyBatchSaveResponse>> BatchSave([FromBody] SysDicCurrencyBatchSaveRequest req) => await service.BatchSaveAsync(req);
}

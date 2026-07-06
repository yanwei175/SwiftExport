using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.WebApi.Filters;

using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>
/// 货币主数据控制器
/// 基类已提供标准 CRUD + BatchSave + GetPagedList，
/// 此处仅补充货币专属业务接口。
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class SysDicCurrencyController(ISysDicCurrencyService service) : ControllerBase
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

    //[HttpGet("defaultbasecurrency")]
    //public async Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrency()
    //{
    //    return await CurrencyService.GetDefaultBaseCurrencyAsync();
    //}
    [HttpGet("{id}")]
    public async Task<ServiceResult<SysDicCurrencyDto>> GetById(Guid id) => await service.GetByIdAsync(id);

    [HttpPost]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<Guid>> Create([FromBody] SysDicCurrencyDto dto) => await service.AddAsync(dto);

    [HttpPut("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Update(Guid id, [FromBody] SysDicCurrencyDto dto) => await service.UpdateAsync(dto);

    [HttpDelete("{id}")]
    [PreventDuplicateRequest]
    public async Task<ServiceResult<int>> Delete(Guid id) => await service.DeleteAsync(id);

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResultDto<SysDicCurrencyDto>>> GetPagedList([FromBody] QueryCriteria criteria, [FromQuery] int pageIndex = 1, [FromQuery] int pageSize = 500)
        => await service.GetPagedListAsync(criteria, pageIndex, pageSize);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SysDicCurrencyBatchSaveResponse>> BatchSave([FromBody] SysDicCurrencyBatchSaveRequest req) => await service.BatchSaveAsync(req);
}

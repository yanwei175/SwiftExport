using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>
/// 货币汇率字典控制器
/// 基类已提供标准 CRUD + BatchSave + GetPagedList，
/// 此处补充全量查询接口。
/// </summary>
public class SysDicCurrencyExchangeController(ISysDicCurrencyExchangeService service)
    : BaseController<SysDicCurrencyExchange, SysDicCurrencyExchangeDto>(service)
{
    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAll()
    {
        return await service.GetAllAsync();
    }
}

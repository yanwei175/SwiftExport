using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.WebApi.Controllers.Base;

namespace SwiftExport.WebApi.Controllers.Systems;

/// <summary>
/// 货币主数据控制器
/// 基类已提供标准 CRUD + BatchSave + GetPagedList，
/// 此处仅补充货币专属业务接口。
/// </summary>
public class SysDicCurrencyController(ISysDicCurrencyService service)
    : BaseController<SysDicCurrency, SysDicCurrencyDto>(service)
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
}

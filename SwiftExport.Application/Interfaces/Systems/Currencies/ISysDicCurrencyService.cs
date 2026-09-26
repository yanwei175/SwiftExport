using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Currencies;
namespace SwiftExport.Application.Interfaces.Systems.Currencies;

/// <summary>
/// 货币主数据服务接口
/// 显式声明货币 CRUD 与货币专属业务方法
/// </summary>
public partial interface ISysDicCurrencyService : IBaseService<SysDicCurrency, SysDicCurrencyDto>
{

    Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SysDicCurrencyDto>>> GetPagedAsync(SysDicCurrencySearchRequest request);
    Task<ServiceResult<SysDicCurrencyBatchSaveResponse>> BatchSaveAsync(SysDicCurrencyBatchSaveRequest batchReq);

    Task<ServiceResult<int>> SetNewBaseCurrencyAsync(SysDicCurrencyDto newSysDicCurrencyDto);

    /// <summary>获取当前本位币信息</summary>
    Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync();

    /// <summary>获取所有启用的货币列表（下拉选择器场景）</summary>
    Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync();

    Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrencyAsync();

}

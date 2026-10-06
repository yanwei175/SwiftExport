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

    /// <summary>切换本位币并将全部汇率重置为1:1；成功结果提醒重新设置汇率，失败不保留部分修改。</summary>
    Task<ServiceResult<int>> SetNewBaseCurrencyAsync(SysDicCurrencyDto newSysDicCurrencyDto);

    /// <summary>获取当前本位币信息</summary>
    Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync();

    /// <summary>获取所有启用的货币列表（下拉选择器场景）</summary>
    Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync();

    Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrencyAsync();

}

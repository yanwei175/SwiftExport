using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 货币主数据服务接口
    /// 继承泛型 CRUD 基础接口，补充货币专属业务方法
    /// </summary>
    public partial interface ISysDicCurrencyService
    {
        Task<ServiceResult<int>> SetNewBaseCurrencyAsync(SysDicCurrencyDto newSysDicCurrencyDto);

        /// <summary>获取当前本位币信息</summary>
        Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync();

        /// <summary>获取所有启用的货币列表（下拉选择器场景）</summary>
        Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync();

        Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrencyAsync();

    }
}

using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 货币主数据 Refit API 接口
    /// 继承泛型 CRUD 基础接口，补充货币专属查询
    /// </summary>
    [Headers("Content-Type: application/json")]
    public interface ISysDicCurrencyApi : IGenericApi<SysDicCurrencyDto>
    {
        /// <summary>获取当前本位币</summary>
        [Get("/base")]
        Task<ServiceResult<SysDicCurrencyDto>> GetBaseCurrencyAsync();

        /// <summary>获取所有启用的货币列表（下拉选择器用）</summary>
        [Get("/enabled")]
        Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetEnabledListAsync();

        [Post("/setnewbase")]
        Task<ServiceResult<int>> SetNewBaseCurrency([Body] SysDicCurrencyDto dto);

        [Get("/defaultbasecurrency")]
         Task<ServiceResult<SysDicCurrencyDetailDto>> GetDefaultBaseCurrency();

    }
}

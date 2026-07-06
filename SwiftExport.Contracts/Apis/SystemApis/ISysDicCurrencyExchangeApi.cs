using Refit;
using SwiftExport.Contracts.Apis.BaseApi;
using SwiftExport.Contracts.DTOs.SystemDtos;
namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 货币汇率字典 Refit API 接口
    /// 继承泛型 CRUD 基础接口，补充全量查询。
    /// </summary>
    [Headers("Content-Type: application/json")]
    public interface ISysDicCurrencyExchangeApi : IGenericApi<SysDicCurrencyExchangeDto>
    {

    }
}

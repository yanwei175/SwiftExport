using Refit;
using SwiftExport.Contracts.DTOs.Updates;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 客户端版本策略 API。
    /// </summary>
    public interface IClientVersionApi
    {
        [Get("/api/client-version")]
        Task<ServiceResult<ClientVersionInfo>> GetAsync();
    }
}

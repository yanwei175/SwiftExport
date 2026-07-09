using Refit;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.SystemApis
{
    /// <summary>
    /// 单据编号 API。
    /// </summary>
    [Headers("Content-Type: application/json")]
    public interface IDocumentNoApi
    {
        /// <summary>
        /// 获取下一个可用单号。
        /// </summary>
        [Post("/next")]
        Task<ServiceResult<DocumentNoResponse>> GetNextAsync([Body] DocumentNoRequest request);
    }
}

using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 单据编号服务接口。
    /// </summary>
    public interface IDocumentNoService
    {
        /// <summary>
        /// 获取下一个可用单号。
        /// </summary>
        Task<ServiceResult<DocumentNoResultDto>> GetNextDocumentNoAsync(DocumentNoRequestDto request);
    }
}

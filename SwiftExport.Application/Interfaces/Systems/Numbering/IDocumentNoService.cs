using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Systems.Numbering;

/// <summary>
/// 单据编号服务接口。
/// </summary>
public interface IDocumentNoService
{
    /// <summary>
    /// 获取下一个可用单号。
    /// </summary>
    Task<ServiceResult<DocumentNoResponse>> GetNextDocumentNoAsync(DocumentNoRequest request);
}

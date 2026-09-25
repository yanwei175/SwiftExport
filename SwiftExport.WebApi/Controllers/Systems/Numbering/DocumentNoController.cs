using SwiftExport.Application.Interfaces.Systems.Numbering;
using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.DTOs.SystemDtos.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Systems.Numbering
{
    /// <summary>
    /// 单据编号控制器。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class DocumentNoController(IDocumentNoService service) : ControllerBase
    {
        /// <summary>
        /// 获取下一个可用单号。
        /// </summary>
        [HttpPost("next")]
        public async Task<ServiceResult<DocumentNoResponse>> GetNextAsync([FromBody] DocumentNoRequest request)
        {
            return await service.GetNextDocumentNoAsync(request);
        }
    }
}

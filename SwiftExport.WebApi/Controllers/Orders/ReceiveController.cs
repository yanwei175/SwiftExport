using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 采购入库控制器。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ReceiveController(IReceiveService receiveService) : ControllerBase
    {
        /// <summary>
        /// 执行采购入库。
        /// </summary>
        [HttpPost("execute-purchase-receive")]
        public async Task<ServiceResult<string>> ExecutePurchaseReceiveAsync([FromBody] PurchaseReceiveExecuteRequestDto request)
        {
            return await receiveService.ExecutePurchaseReceiveAsync(request);
        }
    }
}

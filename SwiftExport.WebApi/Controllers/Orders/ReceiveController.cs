using Microsoft.AspNetCore.Mvc;
using Refit;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.WebApi.Controllers.Orders
{
    /// <summary>
    /// 采购入库控制器。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class ReceiveController(IReceiptService receiveService) : ControllerBase
    {
        /// <summary>
        /// 按供应商与物料类型获取待入库候选行。
        /// </summary>
        [HttpGet("awating-inbound-lines")]
        public async Task<ServiceResult<IReadOnlyList<AwatingInboundDto>>> GetAwatingInboundLinesAsync([FromQuery] string supplierCode, [FromQuery] string? materialType = null)
        {
            return await receiveService.GetAwatingInboundLinesAsync(supplierCode, materialType);
        }

        /// <summary>
        /// 执行采购入库。
        /// </summary>
        [HttpPost("execute-purchase-receive")]
        public async Task<ServiceResult<string>> ExecutePurchaseReceiveAsync([FromBody] PurchaseReceiveExecuteRequestDto request)
        {
            return await receiveService.ExecutePurchaseReceiveAsync(request);
        }

        /// <summary>
        /// 保存采购入库单据。
        /// </summary>
        [HttpPost("save-receive-detail")]
        public async Task<ServiceResult<SaveReceiveDetailResultDto>> SaveDetailAsync([FromBody] SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto> request)
        {
            return await receiveService.SaveDetailAsync(request);
        }




    }
}

using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 采购入库应用服务接口。
    /// </summary>
    public interface IReceiptService
    {
        /// <summary>
        /// 执行采购入库：生成入库单、回写采购行与需求池、更新库存。
        /// </summary>
        Task<ServiceResult<string>> ExecutePurchaseReceiveAsync(PurchaseReceiveExecuteRequestDto request);
    }
}

using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.Warehouses;
using SwiftExport.Contracts.DTOs.Warehouses.Responses;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces
{
    /// <summary>
    /// 采购入库应用服务接口。
    /// </summary>
    public interface IReceiptService
    {
        /// <summary>
        /// 按供应商与物料类型获取待入库候选行。
        /// 默认物料类型为客供件。
        /// </summary>
        Task<ServiceResult<IReadOnlyList<AwatingInboundDto>>> GetAwatingInboundLinesAsync(string supplierCode, string? materialType);

        /// <summary>
        /// 兼容接口：直接执行采购入库（内部会先保存入库单，再执行过账）。
        /// </summary>
        Task<ServiceResult<string>> ExecutePurchaseReceiveAsync(PurchaseReceiveExecuteRequestDto request);

        /// <summary>
        /// 执行采购入库：生成入库单、回写采购行与需求池、更新库存。
        /// </summary>
        Task<ServiceResult<SaveReceiveDetailResult>> PostReceiveDetialAsync(Guid headId);

        Task<ServiceResult<SaveReceiveDetailResult>> SaveDetailAsync(SaveDocumentRequestBase<ReceiveHeadDto, ReceiveLineDto> request);
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders.Trace;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>按采购单稳定身份读取采购至正式入库的只读证据链。</summary>
    public interface IPurchaseOrderTraceApi
    {
        /// <summary>获取采购来源、跟进、正式入库及业务操作历史。</summary>
        [Get("/{purchaseOrderId}")]
        Task<ServiceResult<PurchaseOrderTraceDto>> GetAsync(Guid purchaseOrderId,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}

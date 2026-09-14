using Refit;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Procurement
{
    /// <summary>采购供应商跟进专用 API；只承载跟进查询与追加式反馈。</summary>
    public interface IPurchaseOrderProgressApi
    {
        /// <summary>分页查询跟进工作台需要的采购头摘要。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<PurchaseOrderProgressListItemDto>>> SearchAsync(
            [Body] PurchaseOrderProgressSearchRequest request,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取指定采购单的跟进行及完整反馈历史。</summary>
        [Get("/{purchaseOrderId}/detail")]
        Task<ServiceResult<PurchaseOrderProgressDetailDto>> GetDetailAsync(
            Guid purchaseOrderId,
            CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>为采购行追加一条供应商反馈。</summary>
        [Post("/{purchaseOrderId}/reports")]
        Task<ServiceResult<PurchaseOrderProgressReportResult>> ReportAsync(
            Guid purchaseOrderId,
            [Body] ReportPurchaseOrderProgressCommand command,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}

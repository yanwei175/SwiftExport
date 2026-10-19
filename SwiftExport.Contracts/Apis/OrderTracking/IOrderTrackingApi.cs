using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.DemandDetail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.OrderTracking.Materials;
using SwiftExport.Contracts.DTOs.OrderTracking.Purchases;
using SwiftExport.Contracts.DTOs.OrderTracking.Receipts;
using SwiftExport.Contracts.DTOs.OrderTracking.Deliveries;

namespace SwiftExport.Contracts.Apis.OrderTracking
{
    /// <summary>订单跟踪独立只读查询；客户端 BaseAddress 为 api/order-tracking。</summary>
    public interface IOrderTrackingApi
    {
        /// <summary>按成品需求行分页查询基础事实，不读取执行进度。</summary>
        [Post("/overview/search")]
        Task<ServiceResult<PagedResponseBase<OrderTrackingListItemDto>>> SearchAsync([Body] OrderTrackingSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取现有非软删除成品需求对应的客户代码候选，不要求销售查看权限。</summary>
        [Get("/customer-codes")]
        Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>通过稳定需求身份读取基础明细。</summary>
        [Get("/demands/{grossDemandId}")]
        Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按来源身份分页，统计全部需求及当前条件命中的需求数。</summary>
        [Post("/sources/search")]
        Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync([Body] OrderTrackingSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>独立读取需求的物料分解事实与历史。</summary>
        [Get("/demands/{grossDemandId}/materials")]
        Task<ServiceResult<List<DemandTrackingMaterialDto>>> GetMaterialsAsync(Guid grossDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>独立读取采购关联贡献，不将采购整行数量当作需求贡献。</summary>
        [Get("/demands/{grossDemandId}/purchases")]
        Task<ServiceResult<List<DemandTrackingPurchaseDto>>> GetPurchasesAsync(Guid grossDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>独立读取关联采购行的收货证据，不分摊为本需求收货量。</summary>
        [Get("/demands/{grossDemandId}/receipts")]
        Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetReceiptsAsync(Guid grossDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>独立读取正式交付与反向历史及可信净额。</summary>
        [Get("/demands/{grossDemandId}/deliveries")]
        Task<ServiceResult<DemandTrackingDeliverySectionDto>> GetDeliveriesAsync(Guid grossDemandId, CancellationToken cancellationToken = default(CancellationToken));
    }
}

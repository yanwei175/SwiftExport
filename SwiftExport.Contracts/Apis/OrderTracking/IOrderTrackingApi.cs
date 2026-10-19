using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Refit;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.DTOs.OrderTracking.MaterialDetail;
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


        /// <summary>批量读取当前页来源的活动需求摘要，按维度保护权限并保留失败状态。</summary>
        [Post("/sources/summaries")]
        Task<ServiceResult<OrderTrackingSourceSummaryBatchDto>> GetSourceSummariesAsync([Body] OrderTrackingSourceSummaryRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>全量读取一个来源的需求摘要，不继承筛选条件或分页。</summary>
        [Get("/sources/{sourceType}/{sourceGroupId}/summaries")]
        Task<ServiceResult<OrderTrackingSourceDemandSummariesDto>> GetSourceDemandSummariesAsync(string sourceType, Guid sourceGroupId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取现有非软删除成品需求对应的客户代码候选，不要求销售查看权限。</summary>
        [Get("/customer-codes")]
        Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>通过稳定需求身份读取基础明细。</summary>
        [Get("/demands/{grossDemandId}")]
        Task<ServiceResult<DemandTrackingHeaderDto>> GetDemandHeaderAsync(Guid grossDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按来源身份分页，统计全部需求及当前条件命中的需求数。</summary>
        [Post("/sources/search")]
        Task<ServiceResult<PagedResponseBase<OrderTrackingSourceGroupDto>>> SearchSourcesAsync([Body] OrderTrackingSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按已应用条件读取指定来源中全部命中成品需求身份，不分页，也不裁剪全量子表。</summary>
        [Post("/sources/{sourceType}/{sourceGroupId}/matches")]
        Task<ServiceResult<OrderTrackingSourceMatchesDto>> GetSourceMatchesAsync(string sourceType, Guid sourceGroupId, [Body] OrderTrackingSourceMatchRequest request, CancellationToken cancellationToken = default(CancellationToken));
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
        /// <summary>读取所选来源的全部非删除需求，不应用主表筛选或分页。</summary>
        [Get("/sources/{sourceType}/{sourceGroupId}/demands")]
        Task<ServiceResult<OrderTrackingSourceDemandsDto>> GetSourceDemandsAsync(string sourceType, Guid sourceGroupId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取单个物料需求的快照、来源和回退历史。</summary>
        [Get("/materials/{netDemandId}")]
        Task<ServiceResult<MaterialTrackingDetailDto>> GetMaterialDetailAsync(Guid netDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取本物料的可信采购承接进度及活动和历史关联。</summary>
        [Get("/materials/{netDemandId}/purchases")]
        Task<ServiceResult<MaterialTrackingPurchaseSectionDto>> GetMaterialPurchasesAsync(Guid netDemandId, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>读取本物料关联采购行的整行收货进度及证据，不分摊实收份额。</summary>
        [Get("/materials/{netDemandId}/receipts")]
        Task<ServiceResult<DemandTrackingReceiptSectionDto>> GetMaterialReceiptsAsync(Guid netDemandId, CancellationToken cancellationToken = default(CancellationToken));
    }
}

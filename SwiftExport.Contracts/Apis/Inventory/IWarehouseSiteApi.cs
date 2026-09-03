using Refit;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>客户端 BaseAddress 为 api/inventory/warehouse-sites。</summary>
    public interface IWarehouseSiteApi
    {
        /// <summary>采购新增仓库候选，要求 Admin、Procurement.Order.Create 或 Procurement.Order.Confirm；错误按 HTTP 状态处理。</summary>
        [Post("/candidates/search")]
        Task<ServiceResult<PagedResponseBase<WarehouseCandidateDto>>> SearchCandidatesAsync([Body] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取仓库管理列表，包含停用展示；不得把结果当采购新增资格。</summary>
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync([Body] WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>读取当前仓库显示资料，不替代采购历史快照。</summary>
        [Get("/{uid}")]
        Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>显式建立供应商虚拟仓，不由采购候选查询调用。</summary>
        [Post("/supplier-virtual")]
        Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync([Body] CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>更新允许编辑的仓库资料。</summary>
        [Put("/{uid}")]
        Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, [Body] UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default(CancellationToken));
        /// <summary>按现有约束停用仓库，保留历史事实。</summary>
        [Post("/{uid}/deactivate")]
        Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, [Body] DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}

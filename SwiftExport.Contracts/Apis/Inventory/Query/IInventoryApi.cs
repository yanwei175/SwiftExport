using Refit;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SwiftExport.Contracts.Apis.Inventory
{
    /// <summary>客户端 BaseAddress 为 api/inventory。</summary>
    public interface IInventoryApi
    {
        /// <summary>按库存查看权限分页读取未删除供应商的筛选身份；包含停用供应商，不依赖主数据管理权限或已有库存记录。</summary>
        [Get("/suppliers/picker")]
        Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> GetSupplierOptionsAsync(
            [Query] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

        /// <summary>按库存余额与流水查看权限分页读取活动仓库筛选项，不授予仓库管理或过账权限。</summary>
        [Get("/warehouses/picker")]
        Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehouseOptionsAsync([Query] PagedRequestBase request, CancellationToken cancellationToken = default(CancellationToken));

        [Post("/balances/search")]
        Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync([Body] InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/ledger/search")]
        Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync([Body] InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/balances/{uid}/trace")]
        Task<ServiceResult<InventoryBalanceTraceDto>> GetTraceAsync(Guid uid, [Query] int pageIndex = 1, [Query] int pageSize = 50, CancellationToken cancellationToken = default(CancellationToken));
    }
}

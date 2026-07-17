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
        [Post("/search")]
        Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchAsync([Body] WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default(CancellationToken));
        [Get("/{uid}")]
        Task<ServiceResult<WarehouseSiteDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/supplier-virtual")]
        Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualAsync([Body] CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Put("/{uid}")]
        Task<ServiceResult<WarehouseSiteDto>> UpdateAsync(Guid uid, [Body] UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default(CancellationToken));
        [Post("/{uid}/deactivate")]
        Task<ServiceResult<WarehouseSiteDto>> DeactivateAsync(Guid uid, [Body] DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default(CancellationToken));
    }
}

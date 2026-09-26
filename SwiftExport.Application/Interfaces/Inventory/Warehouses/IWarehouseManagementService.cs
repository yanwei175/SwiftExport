using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Inventory.Warehouses;

/// <summary>提供供应商虚拟仓的显式维护命令，保留原有版本、权限和命令幂等边界。</summary>
public interface IWarehouseManagementService
{
    /// <summary>为活动供应商创建唯一活动虚拟仓。</summary>
    Task<ServiceResult<WarehouseSiteDto>> CreateSupplierVirtualWarehouseAsync(CreateSupplierVirtualWarehouseCommand command, CancellationToken cancellationToken = default);

    /// <summary>按乐观并发版本修改虚拟仓名称。</summary>
    Task<ServiceResult<WarehouseSiteDto>> UpdateWarehouseSiteAsync(Guid uid, UpdateWarehouseSiteCommand command, CancellationToken cancellationToken = default);

    /// <summary>在不存在有效库存或历史流水时停用虚拟仓。</summary>
    Task<ServiceResult<WarehouseSiteDto>> DeactivateWarehouseSiteAsync(Guid uid, DeactivateWarehouseSiteCommand command, CancellationToken cancellationToken = default);

    /// <summary>按乐观并发版本重新启用未删除虚拟仓，并保持供应商活动仓唯一。</summary>
    Task<ServiceResult<WarehouseSiteDto>> ActivateWarehouseSiteAsync(Guid uid, ActivateWarehouseSiteCommand command, CancellationToken cancellationToken = default);

    /// <summary>软删除虚拟仓；仅允许不存在余额与流水时删除。</summary>
    Task<ServiceResult<WarehouseSiteDto>> DeleteWarehouseSiteAsync(Guid uid, DeleteWarehouseSiteCommand command, CancellationToken cancellationToken = default);
}

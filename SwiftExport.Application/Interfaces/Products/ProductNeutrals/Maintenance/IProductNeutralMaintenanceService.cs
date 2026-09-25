using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductNeutrals.Maintenance;

/// <summary>维护指定主型号下的中性产品，不更新父型号字段。</summary>
public interface IProductNeutralMaintenanceService
{
    /// <summary>原子保存指定主型号下的中性产品变更。</summary>
    Task<ServiceResult<ProductMasterDetailDto>> SaveNeutralsAsync(Guid productMasterId, BatchSaveRequestBase<ProductNeutralWriteDto> request, CancellationToken cancellationToken = default);
}

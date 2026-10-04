using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Services.Products.ProductMasters.Maintenance;

/// <summary>主型号事务内的中性产品保存入口，与主从保存共享事务编排。</summary>
public sealed partial class ProductMasterMaintenanceService
{
    /// <summary>在主型号事务中仅保存中性产品，保留父型号字段和版本。</summary>
    public async Task<ServiceResult<ProductMasterDetailDto>> SaveNeutralsAsync(Guid productMasterId, BatchSaveRequestBase<ProductNeutralWriteDto> request, CancellationToken cancellationToken = default)
    {
        if (productMasterId == Guid.Empty || request is null)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.Validation, "主型号和中性产品变更不能为空。");
        return await ExecuteTransactionAsync(async () =>
        {
            NormalizeNeutralChanges(request);
            if (!request.HasChanges)
                throw new ProductMasterWriteException(ServiceErrorCodes.Validation, "没有需要保存的中性产品变更。");
            await LockNeutralColorsAsync(request, cancellationToken);
            var existing = await LoadDetailAsync(productMasterId, true, cancellationToken)
                ?? throw new ProductMasterWriteException(ServiceErrorCodes.BusinessBlocked, "主型号不存在或已删除。");
            await ValidateNeutralChangesAsync(request, existing, cancellationToken);
            await SaveNeutralRowsAsync(productMasterId, request);
            var saved = await LoadDetailAsync(productMasterId, false, cancellationToken)
                ?? throw new ProductMasterWriteException(ServiceErrorCodes.Unexpected, "保存后无法读取主型号详情。");
            return ServiceResult<ProductMasterDetailDto>.Ok(saved, "中性产品已保存。");
        }, "保存中性产品", cancellationToken);
    }

}

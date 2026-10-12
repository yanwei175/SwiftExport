using SwiftExport.Contracts.DTOs.Products.ProductMasters.Import;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.ProductMasters.Import;

/// <summary>负责管理员父级平表导入，仅写主型号，每行独立事务。</summary>
public interface IProductMasterImportService
{
    /// <summary>校验有界批次并返回逐行保存回执；技术中断时可能已有行提交。</summary>
    Task<ServiceResult<BatchSaveResponseBase>> ImportAsync(List<ProductMasterImportRowDto> rows, CancellationToken cancellationToken = default);
}

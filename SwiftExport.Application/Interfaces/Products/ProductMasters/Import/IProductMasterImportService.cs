using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.Products.ProductMasters.Import;
/// <summary>只读匹配及独立父级写入，不进入原主从同步保存链。</summary>
public interface IProductMasterImportService
{
    /// <summary>校验最多一千行输入。</summary>
    Task<ServiceResult<ProductMasterImportPreviewResult>> PreviewAsync(ProductMasterImportPreviewRequest request, CancellationToken cancellationToken = default);
    /// <summary>检查活动管理员及匹配依据后原子保存。</summary>
    Task<ServiceResult<ProductMasterImportResult>> ImportAsync(ProductMasterImportRequest request, CancellationToken cancellationToken = default);
}

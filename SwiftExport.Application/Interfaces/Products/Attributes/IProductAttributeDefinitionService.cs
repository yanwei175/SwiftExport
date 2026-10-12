using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.Attributes;

/// <summary>分类标签定义的完整读取与同分类原子维护用例。</summary>
public interface IProductAttributeDefinitionService
{
    /// <summary>读取全部或单一分类定义，正常空定义仍成功返回。</summary>
    Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken = default);
    /// <summary>在分类父锁内保存组与选项，任一失败回滚全部变更。</summary>
    Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default);
}

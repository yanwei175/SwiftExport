using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.Attributes;

/// <summary>共享标签定义、分类继承读取及全局原子维护用例。</summary>
public interface IProductAttributeDefinitionService
{
    /// <summary>读取全局定义或具体分类及祖先提供的定义和直接来源关系，正常空定义仍成功返回。</summary>
    Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? categoryId, CancellationToken cancellationToken = default);
    /// <summary>按分类、组、选项顺序锁定并保存全页组、选项及关联，任一失败回滚全部变更。</summary>
    Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default);
    /// <summary>兼容旧端点仅返回升级提示，禁止把分类内旧草稿解释为全局定义变更。</summary>
    Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveAsync(Guid categoryId, SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default);
}

using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.Attributes;

/// <summary>共享标签定义、属性模板适用范围读取及全局原子维护用例。</summary>
public interface IProductAttributeDefinitionService
{
    /// <summary>读取全局定义或具体属性模板直接关联的定义，正常空定义仍成功返回。</summary>
    Task<ServiceResult<ProductAttributeDefinitionsDto>> GetDefinitionsAsync(Guid? profileId, CancellationToken cancellationToken = default);
    /// <summary>按模板、组、选项顺序锁定并保存全页定义及关联，任一失败回滚全部变更。</summary>
    Task<ServiceResult<SaveProductAttributeDefinitionsResponse>> BatchSaveDefinitionsAsync(SaveProductAttributeDefinitionsRequest request, CancellationToken cancellationToken = default);
}

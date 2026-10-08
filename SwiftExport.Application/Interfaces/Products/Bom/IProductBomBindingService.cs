using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.BomManagement;

/// <summary>按版本建立或绑定共享 BOM，不承担组成保存，也不改写其他产品字段。</summary>
public interface IProductBomBindingService
{
    /// <summary>在自有事务内授权、验证共享结构并保存绑定；未知提交结果不得自动重试。</summary>
    Task<ServiceResult<SaveItemResult>> BindAsync(Guid productId, ProductBomBindingRequest request, CancellationToken cancellationToken);
}

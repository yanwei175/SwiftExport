using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExportClient.Features.Products.Bom.Services;

/// <summary>协调编号预览与窄绑定提交，不持有页面状态或弹窗。</summary>
public interface IBomBindingClientService
{
    /// <summary>读取编号摘要，保持调用者取消语义。</summary>
    Task<ServiceResult<BomNumberInfoDto>> PreviewAsync(string bomNo, CancellationToken cancellationToken);
    /// <summary>提交前再次预览并调用窄绑定接口，未知结果不重试。</summary>
    Task<ServiceResult<SaveItemResult>> BindAsync(Guid productId, ProductBomBindingRequest request);
}

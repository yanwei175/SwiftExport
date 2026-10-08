using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExportClient.Services.Api;

namespace SwiftExportClient.Features.Products.Bom.Services;

/// <summary>在绑定提交前核对编号模式，正式资格仍由后端事务权威复核。</summary>
public sealed class BomBindingClientService(IBomApi bomApi, IProductBomBindingApi bindingApi, IApiCallRunner runner) : IBomBindingClientService
{
    /// <inheritdoc />
    public Task<ServiceResult<BomNumberInfoDto>> PreviewAsync(string bomNo, CancellationToken cancellationToken) =>
        runner.RunAsync(() => bomApi.GetNumberInfoAsync(bomNo, cancellationToken), cancellationToken, "查询 BOM 编号失败");

    /// <inheritdoc />
    public async Task<ServiceResult<SaveItemResult>> BindAsync(Guid productId, ProductBomBindingRequest request)
    {
        var preview = await PreviewAsync(request.BomNo, CancellationToken.None);
        if (!preview.Success || preview.Data is null)
            // 此时尚未发出写入，读取超时不应被表达为绑定结果未知。
            return ServiceResult<SaveItemResult>.Fail(preview.Error?.Code == ServiceErrorCodes.OutcomeUnknown
                ? ServiceErrorCodes.ServiceUnavailable : preview.Error?.Code ?? ServiceErrorCodes.Unexpected, preview.Message);
        var info = preview.Data;
        if (request.BindExisting ? info.BoundProductCount == 0 || info.ComponentCount == 0 || info.HasInvalidComponents
            : info.BoundProductCount > 0 || info.StoredComponentCount > 0)
            return ServiceResult<SaveItemResult>.Fail(ServiceErrorCodes.BusinessBlocked,
                request.BindExisting ? "目标没有可绑定的有效共享组成。" : "编号已有绑定或组成，请更换编号或选择绑定已有 BOM。");
        return await runner.RunAsync(() => bindingApi.BindAsync(productId, request), "保存 BOM 绑定失败");
    }
}

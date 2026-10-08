using Microsoft.Extensions.Logging;
using SwiftExport.Application.Interfaces.Products.BomManagement;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Products.BomManagement;

namespace SwiftExport.Application.Services.Products.BomManagement;

/// <summary>校验管理查询输入并统一读取失败反馈；只读查询不创建业务事务。</summary>
public sealed class BomManagementQueryService(IBomManagementQueryRepository repository, ILogger<BomManagementQueryService> logger) : IBomManagementQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<BomProductListItemDto>>> SearchProductsAsync(BomProductSearchRequest request, CancellationToken cancellationToken)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200
            || request.CategoryIds?.Count > 300 || request.ColorIds?.Count > 300
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue)
            return ServiceResult<PagedResponseBase<BomProductListItemDto>>.Fail(ServiceErrorCodes.Validation, "BOM 产品查询条件无效。");
        try { return ServiceResult<PagedResponseBase<BomProductListItemDto>>.Ok(await repository.SearchProductsAsync(request, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "读取 BOM 管理成品失败。");
            return ServiceResult<PagedResponseBase<BomProductListItemDto>>.Fail(ServiceErrorCodes.Unexpected, "读取 BOM 成品失败，请稍后重试。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<BomNumberInfoDto>> GetNumberInfoAsync(string bomNo, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(bomNo) || bomNo.Length > 200)
            return ServiceResult<BomNumberInfoDto>.Fail(ServiceErrorCodes.Validation, "请输入有效 BOM 编号。");
        try { return ServiceResult<BomNumberInfoDto>.Ok(await repository.GetNumberInfoAsync(bomNo, cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            logger.LogError(exception, "读取 BOM 编号摘要失败。");
            return ServiceResult<BomNumberInfoDto>.Fail(ServiceErrorCodes.Unexpected, "读取 BOM 编号失败，请稍后重试。");
        }
    }
}

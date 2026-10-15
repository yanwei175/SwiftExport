using SwiftExport.Core.Interfaces.Products.Catalog;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Auth;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Products.Catalog.Query;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System.Data;

namespace SwiftExport.Application.Services.Products.Catalog.Query;

/// <summary>读取产品详情和选择器投影，不开启写入事务。</summary>
public sealed class ProductQueryService(
    IProductRepository repository, IWarehouseQueryRepository warehouses,
    ICurrentUser currentUser, IPermissionEvaluationService permissions,
    ServiceBaseContext context, IProductAttributeRepository attributeRepository) : IProductQueryService
{
    private readonly ILogger<ProductQueryService> _logger = context.LoggerFactory.CreateLogger<ProductQueryService>();

    /// <inheritdoc />
    public async Task<ServiceResult<ProductDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<ProductDetailDto>.Fail(ServiceErrorCodes.Validation, "物料身份无效。");
        IDbTransaction? read = null;
        try
        {
            // 三类读取共享一致快照，避免把上游旧模板和物料新选择拼成同一详情；参加调用方事务时不持有提交权。
            if (context.Session.Transaction is null)
            {
                read = context.Session.Connection.BeginTransaction(IsolationLevel.RepeatableRead);
                context.Session.Transaction = read;
            }
            var detail = await repository.GetDetailAsync(uid, cancellationToken);
            if (detail is null) return ServiceResult<ProductDetailDto>.Fail(ServiceErrorCodes.NotFound, "物料不存在或已删除。");
            detail.SelectedOptionIds = (await attributeRepository.GetSelectionsAsync(ProductAttributeOwnerType.Product, [uid], cancellationToken))
                .Select(row => row.OptionId).OrderBy(id => id).ToList();
            detail.AttributeSelections = (await attributeRepository.GetSelectionDetailsAsync(ProductAttributeOwnerType.Product, [uid], cancellationToken)).ToList();
            detail.Packages = (await repository.GetPackagesAsync(uid, cancellationToken: cancellationToken)).ToList();
            read?.Commit();
            return ServiceResult<ProductDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "读取物料编辑详情失败。");
            return ServiceResult<ProductDetailDto>.Fail(ServiceErrorCodes.Unexpected, "读取物料详情失败，请稍后重试。");
        }
        finally
        {
            if (read is not null)
            {
                try { read.Dispose(); }
                finally { context.Session.Transaction = null; }
            }
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProductDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var dto = await repository.GetDtoByIdAsync(uid);
            return dto is null
                ? ServiceResult<ProductDto>.Fail(ServiceErrorCodes.NotFound, "未找到数据")
                : ServiceResult<ProductDto>.Ok(dto);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<ProductDto>.Fail(ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductDto>>> GetPagedAsync(ProductSearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await repository.GetPagedAsync(request, cancellationToken);
            return result is null
                ? ServiceResult<PagedResponseBase<ProductDto>>.Fail("未找到数据")
                : ServiceResult<PagedResponseBase<ProductDto>>.Ok(result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "应用服务执行失败。");
            return ServiceResult<PagedResponseBase<ProductDto>>.Fail(ServiceErrorCodes.Unexpected, "系统发生内部错误，请联系管理员。");
        }
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku)
    {
        var dto = await repository.GetDtoBySkuAsync(sku);
        return dto is null
            ? ServiceResult<ProductDto>.Fail($"未找到 SKU 为 {sku} 的产品")
            : ServiceResult<ProductDto>.Ok(dto);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<List<ProductLookupDto>>> SearchLookupAsync(ProductLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Keyword))
            return ServiceResult<List<ProductLookupDto>>.Ok([]);
        var items = await repository.SearchLookupAsync(request, cancellationToken);
        return ServiceResult<List<ProductLookupDto>>.Ok(items.ToList());
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync()
    {
        var list = await repository.GetAllCustomerCodeListAsync();
        return list is null || list.Count == 0
            ? ServiceResult<IReadOnlyList<string>>.Fail("空的客户代码集合")
            : ServiceResult<IReadOnlyList<string>>.Ok(list);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>> GetDefaultWarehouseCandidatesAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        // 产品页只能取得精简参考候选；先授权再验证、查询，避免回用库存模块权限或读取库存资料。
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId
            || !await permissions.HasPermissionAsync(userId, "Products.Product.View", cancellationToken))
            return ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Fail(
                ServiceErrorCodes.Forbidden, "当前账号无产品模块访问权限。");
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200
            || request.Keyword?.Length > 200)
            return ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Fail(
                ServiceErrorCodes.Validation, "仓库候选查询条件无效。");

        try
        {
            var page = await warehouses.GetProductDefaultCandidatesAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, cancellationToken);
            return ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Ok(page);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            _logger.LogError(exception, "读取产品默认仓库候选失败。");
            return ServiceResult<PagedResponseBase<ProductDefaultWarehouseCandidateDto>>.Fail(
                ServiceErrorCodes.Unexpected, "读取产品默认仓库候选失败，请稍后重试。");
        }
    }
}

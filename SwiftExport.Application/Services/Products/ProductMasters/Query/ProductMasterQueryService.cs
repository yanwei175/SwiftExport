using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;
using SwiftExport.Application.Interfaces.Products.ProductMasters.Query;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces;
using Mapster;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Products.ProductMasters.Import;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Products.Categories;
using SwiftExport.Core.Interfaces.Products.Attributes;
using SwiftExport.Core.Interfaces.Suppliers.Management;

namespace SwiftExport.Application.Services.Products.ProductMasters.Query;

/// <summary>读取主型号分页标签摘要，以及包含中性产品和已选标签的聚合详情，不拥有写入事务。</summary>
public sealed class ProductMasterQueryService(IProductMasterRepository masterRepository, IProductNeutralRepository neutralRepository,
    IProductCategoryRepository categories, ISupplierRepository suppliers, ICurrentUser currentUser, IPermissionEvaluationService permissions,
    IProductAttributeRepository attributes) : IProductMasterQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResponseBase<ProductMasterListItemDto>>> GetPagedAsync(ProductMasterSearchRequest request, CancellationToken cancellationToken = default)
    {
        var page = await masterRepository.GetPagedAsync(request ?? new ProductMasterSearchRequest(), cancellationToken);
        // 当前页一次读取选择，不为每一行单独查询，也不把摘要回写旧字符串字段。
        var selected = (await attributes.GetSelectionDetailsAsync(page.Items.Select(row => row.Uid), cancellationToken)).ToLookup(row => row.ProductMasterId);
        foreach (var row in page.Items)
            row.TagSummary = string.Join("；", selected[row.Uid].GroupBy(value => value.AttributeGroupId)
                .OrderBy(group => group.First().GroupSortOrder).ThenBy(group => group.Key)
                .Select(group => group.First().GroupName + "：" + string.Join("、", group.OrderBy(value => value.OptionSortOrder).ThenBy(value => value.OptionId)
                    .Select(value => value.OptionName + (value.IsAvailable ? "" : "（已失效）")))));
        return ServiceResult<PagedResponseBase<ProductMasterListItemDto>>.Ok(page);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<ProductMasterDetailDto>> GetDetailAsync(Guid productMasterId, CancellationToken cancellationToken = default)
    {
        if (productMasterId == Guid.Empty)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.Validation, "主型号标识不能为空。", nameof(productMasterId));
        var detail = await masterRepository.GetDetailHeaderAsync(productMasterId, false, cancellationToken);
        if (detail is null)
            return ServiceResult<ProductMasterDetailDto>.Fail(ServiceErrorCodes.BusinessBlocked, "主型号不存在或已删除。");
        detail.Neutrals = (await neutralRepository.GetByProductMasterIdAsync(productMasterId, false, cancellationToken)).ToList();
        detail.AttributeSelections = (await attributes.GetSelectionDetailsAsync([productMasterId], cancellationToken)).ToList();
        detail.SelectedOptionIds = detail.AttributeSelections.Select(row => row.OptionId).ToList();
        return ServiceResult<ProductMasterDetailDto>.Ok(detail);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<List<ProductMasterExportRowDto>>> ExportAsync(ProductMasterExportRequest request, CancellationToken cancellationToken = default)
    {
        var id = currentUser.UserId ?? Guid.Empty;
        var projection = currentUser.IsAuthenticated && id != Guid.Empty ? await permissions.GetProjectionAsync(id, cancellationToken) : null;
        if (projection is null) return ServiceResult<List<ProductMasterExportRowDto>>.Fail(ServiceErrorCodes.Unauthenticated, "登录状态无效。");
        if (!projection.IsAdmin && !projection.Permissions.Contains(PermissionKeys.ProductsMasterView, StringComparer.Ordinal))
            return ServiceResult<List<ProductMasterExportRowDto>>.Fail(ServiceErrorCodes.Forbidden, "缺少父级型号查看权限。");
        if (request is null || request.CategoryIds is { Count: > 10000 } || request.Keyword?.Length > 2000)
            return ServiceResult<List<ProductMasterExportRowDto>>.Fail(ServiceErrorCodes.Validation, "导出筛选无效。");
        var rows = await masterRepository.GetForExportAsync(new ProductMasterSearchRequest
        { Keyword = request.Keyword, CategoryIds = request.CategoryIds ?? [], IncludeUncategorized = request.IncludeUncategorized, ExcludeCategoryId = request.ExcludeCategoryId }, 10000, cancellationToken);
        if (rows.Count > 10000) return ServiceResult<List<ProductMasterExportRowDto>>.Fail(ServiceErrorCodes.Validation, "匹配资料超过 10000 行，请缩小筛选范围。");
        var paths = ProductMasterReferencePaths.Build(await categories.GetAllAsync());
        var ids = rows.Select(row => row.DefaultSupplierId).Where(value => value != Guid.Empty).Distinct().ToArray();
        var supplierRows = ids.Length == 0 ? [] : await suppliers.GetByIdListAsync(ids);
        var result = new List<ProductMasterExportRowDto>();
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = string.Empty;
            if (row.CategoryId != Guid.Empty && (!paths.TryGetValue(row.CategoryId, out path!) || path is null))
                return ServiceResult<List<ProductMasterExportRowDto>>.Fail(ServiceErrorCodes.BusinessBlocked, "部分主型号的分类引用或层级失效，请修正后导出。");
            var supplier = row.DefaultSupplierId == Guid.Empty ? null : supplierRows.SingleOrDefault(value => value.Uid == row.DefaultSupplierId && !value.IsDeleted);
            if (row.DefaultSupplierId != Guid.Empty && supplier is null)
                return ServiceResult<List<ProductMasterExportRowDto>>.Fail(ServiceErrorCodes.BusinessBlocked, "部分主型号的默认供应商引用失效，请修正后导出。");
            result.Add(new() { Data = row.Adapt<ProductMasterTransferData>(), CategoryPath = path, DefaultSupplierCode = supplier?.SupplierCode ?? string.Empty });
        }
        return ServiceResult<List<ProductMasterExportRowDto>>.Ok(result);
    }
}

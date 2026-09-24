using SwiftExport.Application.Services.Inventory.Posting;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
namespace SwiftExport.Application.Services.Inventory.Warehouses;

/// <summary>按库存模块访问权限读取仓库资料、供应商候选与活动仓库选择项，不写入仓库或库存事实。</summary>
public sealed class WarehouseQueryService(
    ServiceBaseContext context, IWarehouseQueryRepository warehouseSites,
    ICurrentUser currentUser, IUserPermissionService permissions) : IWarehouseQueryService
{
    private const string ReadPermissionDeniedMessage = "当前账号无库存模块访问权限。";
    private readonly ILogger<WarehouseQueryService> _logger = context.LoggerFactory.CreateLogger<WarehouseQueryService>();
    private readonly IUserPermissionService _permissions = permissions;

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>> GetWarehousePickerPagedAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            return ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>.Fail("仓库选择查询条件无效。");
        return await ReadWarehouseAsync(
            () => warehouseSites.GetPickerPagedAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, cancellationToken), cancellationToken, "查询仓库选择候选失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>> SearchWarehouseSuppliersAsync(PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 100 || request.Keyword?.Length > 100)
            return ServiceResult<PagedResponseBase<WarehouseSupplierLookupDto>>.Fail("供应商查询条件无效。");
        return await ReadWarehouseAsync(
            () => warehouseSites.SearchSupplierLookupAsync(new PagedRequestBase
            {
                Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
            }, cancellationToken), cancellationToken, "查询供应商候选失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<WarehouseSiteDto>>> SearchWarehouseSitesAsync(WarehouseSiteSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 100)
            return ServiceResult<PagedResponseBase<WarehouseSiteDto>>.Fail("虚拟仓查询条件无效。");
        return await ReadWarehouseAsync(() => warehouseSites.SearchAsync(request, cancellationToken), cancellationToken, "查询供应商虚拟仓失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<WarehouseSiteDto>> GetWarehouseSiteAsync(Guid uid, CancellationToken cancellationToken = default) =>
        uid == Guid.Empty
            ? ServiceResult<WarehouseSiteDto>.Fail("虚拟仓 Uid 不能为空。")
            : await ReadWarehouseAsync(async () => await warehouseSites.GetDtoAsync(uid, cancellationToken)
                ?? throw new InventoryPostingException("虚拟仓不存在。"), cancellationToken, "加载虚拟仓失败。");
    /// <summary>在读取仓库资料前执行模块访问授权；前端菜单权限不作为安全边界。</summary>
    private async Task<ServiceResult<T>> ReadWarehouseAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken, string failure)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } userId
            || !await _permissions.HasPermissionAsync(userId, PermissionKeys.ModuleInventoryAccess, cancellationToken))
            return ServiceResult<T>.Fail(ServiceErrorCodes.Forbidden, ReadPermissionDeniedMessage);
        return await ReadAsync(action, failure);
    }

    /// <summary>保留原有查询异常映射，取消请求交由上层处理。</summary>
    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action, string failure)
    {
        try { return ServiceResult<T>.Ok(await action()); } catch (OperationCanceledException) { throw; } catch (InventoryPostingException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message); } catch (Exception ex) { _logger.LogError(ex, "{Failure}", failure); return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, failure); }
    }
}

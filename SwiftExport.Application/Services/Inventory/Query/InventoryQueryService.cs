using SwiftExport.Application.Services.Inventory.Posting;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Inventory.Query;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Query;
namespace SwiftExport.Application.Services.Inventory.Query;

/// <summary>按库存模块访问权限读取余额、不可变流水与余额追溯，不持有写入事务。</summary>
public sealed class InventoryQueryService(
    ServiceBaseContext context, IInventoryQueryRepository inventory,
    ICurrentUser currentUser, IPermissionEvaluationService permissions) : IInventoryQueryService
{
    private const string ReadPermissionDeniedMessage = "当前账号无库存模块访问权限。";
    private readonly ILogger<InventoryQueryService> _logger = context.LoggerFactory.CreateLogger<InventoryQueryService>();
    private readonly IPermissionEvaluationService _permissions = permissions;

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<InventoryBalanceDto>>> SearchBalancesAsync(InventoryBalanceSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200)
            return ServiceResult<PagedResponseBase<InventoryBalanceDto>>.Fail("库存余额查询条件无效。");
        return await ReadWarehouseAsync(() => inventory.SearchBalancesAsync(request, cancellationToken), cancellationToken, "查询库存余额失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>> SearchLedgerAsync(InventoryLedgerSearchRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 200 || request.Keyword?.Length > 200
            || request.SourceDocumentNo?.Length > 100 || request.OccurredDateFrom > request.OccurredDateTo)
            return ServiceResult<PagedResponseBase<InventoryLedgerItemDto>>.Fail("库存流水查询条件无效。");
        return await ReadWarehouseAsync(() => inventory.SearchLedgerAsync(request, cancellationToken), cancellationToken, "查询库存流水失败。");
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<InventoryBalanceTraceDto>> GetBalanceTraceAsync(Guid uid, int pageIndex, int pageSize, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || pageIndex < 1 || pageSize is < 1 or > 200)
            return ServiceResult<InventoryBalanceTraceDto>.Fail("库存余额 Uid 或追溯分页参数无效。");
        return await ReadWarehouseAsync(async () =>
        {
            var balance = await inventory.GetBalanceDtoAsync(uid, cancellationToken)
                ?? throw new InventoryPostingException("库存余额不存在。");
            var ledgerPage = await inventory.GetLedgerByBalanceAsync(uid, pageIndex, pageSize, cancellationToken);
            return new InventoryBalanceTraceDto
            {
                Balance = balance,
                Ledgers = ledgerPage.Items,
                TotalCount = ledgerPage.TotalCount,
                PageIndex = ledgerPage.PageIndex,
                PageSize = ledgerPage.PageSize,
                Reconciliation = await inventory.GetReconciliationAsync(uid, cancellationToken)
                    ?? throw new InventoryPostingException("库存对账信息不存在。")
            };
        }, cancellationToken, "加载库存追溯失败。");
    }

    /// <summary>在读取库存数据前执行模块访问授权；前端菜单权限不作为安全边界。</summary>
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

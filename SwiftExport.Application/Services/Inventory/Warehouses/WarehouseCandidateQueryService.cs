using System.Data;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Inventory.Warehouses;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;

namespace SwiftExport.Application.Services.Inventory.Warehouses;

/// <summary>采购仓库候选查询；权限与分页在同一只读事务内，不形成采购承诺或库存预占。</summary>
public sealed class WarehouseCandidateQueryService(
    ServiceBaseContext context,
    IWarehouseCandidateRepository repository,
    IPermissionEvaluationService permissions,
    ICurrentUser user) : IWarehouseCandidateQueryService
{
    /// <summary>校验身份及分页，重验权限后查询；取消或失败释放事务，不修改调用方请求。</summary>
    public async Task<PagedResponseBase<WarehouseCandidateDto>> SearchAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId is not { } userId || userId == Guid.Empty)
            throw new UnauthorizedAccessException("需要真实有效的采购用户。");
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 100
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue || request.Keyword?.Length > 100)
            throw new ArgumentException("页码、分页大小或关键词无效。", nameof(request));
        var normalized = new PagedRequestBase
        {
            Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
        };
        if (context.Session.Transaction is not null)
            throw new InvalidOperationException("仓库候选查询不能接管已有事务。");
        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            // 候选查询沿用采购创建或确认资格；读取仓库不赋予采购写权限。
            var projection = await permissions.GetProjectionAsync(userId, cancellationToken);
            if (projection is null
                || (!projection.Permissions.Contains(PermissionKeys.ProcurementOrderCreate, StringComparer.Ordinal)
                    && !projection.Permissions.Contains(PermissionKeys.ProcurementOrderConfirm, StringComparer.Ordinal)))
                throw new UnauthorizedAccessException("没有仓库候选查询权限。");
            var result = await repository.SearchAsync(normalized, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            transaction.Commit();
            return result;
        }
        finally
        {
            context.Session.Transaction = null;
        }
    }
}

using System.Data;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.MasterData;

namespace SwiftExport.Application.Services.MasterData;

/// <summary>独立采购主数据窄查询；按用例验证真实权限，候选只作选择提示，不形成任何业务承诺。</summary>
public sealed class ProcurementMasterDataQueryService(
    ServiceBaseContext context, IProcurementMasterDataQueryRepository repository, ICurrentUser user)
    : IProcurementMasterDataQueryService
{
    /// <summary>读取客户新增候选；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "Procurement.Order.Create", repository.SearchCustomersAsync, cancellationToken);

    /// <summary>读取供应商新增候选；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "Procurement.Order.Create", repository.SearchSuppliersAsync, cancellationToken);

    /// <summary>读取标准采购产品新增候选；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "Procurement.Order.Create", repository.SearchProductsAsync, cancellationToken);

    /// <summary>读取客户历史筛选项；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "Procurement.Order.Read", repository.SearchCustomerHistoryFiltersAsync, cancellationToken);

    /// <summary>读取供应商历史筛选项；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, "Procurement.Order.Read", repository.SearchSupplierHistoryFiltersAsync, cancellationToken);

    /// <summary>拒绝匿名及无效输入；授权依据和分页读取保持一致，失败或取消由事务释放回滚。</summary>
    private async Task<PagedResponseBase<T>> ExecuteAsync<T>(PagedRequestBase request, string role,
        Func<PagedRequestBase, CancellationToken, Task<PagedResponseBase<T>>> query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || user.UserId is not { } userId || userId == Guid.Empty)
            throw new UnauthorizedAccessException("需要真实有效的采购用户。");
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 100
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue
            || request.Keyword?.Length > 100)
            throw new ArgumentException("页码须为正数，每页1至100项，关键词不超过100字，分页偏移不得溢出。", nameof(request));

        // 不修改调用方对象；空关键词允许浏览第一页，长输入不静默截断。
        var normalized = new PagedRequestBase
        {
            Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
        };
        if (context.Session.Transaction is not null)
            throw new InvalidOperationException("候选查询须独立拥有只读事务。");
        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            if (!await repository.HasPermissionAsync(userId, role, cancellationToken))
                throw new UnauthorizedAccessException("没有当前候选查询权限。");
            var result = await query(normalized, cancellationToken);
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

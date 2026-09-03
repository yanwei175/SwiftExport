using System.Data;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.MasterData;

namespace SwiftExport.Application.Services.MasterData;

/// <summary>独立采购主数据窄查询；验证登录身份和分页输入，候选只作选择提示，不形成业务承诺。</summary>
public sealed class ProcurementMasterDataQueryService(
    ServiceBaseContext context, IProcurementMasterDataQueryRepository repository, ICurrentUser user)
    : IProcurementMasterDataQueryService
{
    /// <summary>读取客户新增候选；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, repository.SearchCustomersAsync, cancellationToken);

    /// <summary>读取供应商新增候选；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, repository.SearchSuppliersAsync, cancellationToken);

    /// <summary>读取采购供货产品新增候选；身份和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(
        ProcurementProductSearchRequest request, CancellationToken cancellationToken = default)
        => ExecuteProductAsync(request, cancellationToken);

    /// <summary>保留采购产品查询的客户和供应商上下文，同时复用候选查询的权限与事务边界。</summary>
    private async Task<PagedResponseBase<ProcurementProductCandidateDto>> ExecuteProductAsync(
        ProcurementProductSearchRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        var normalized = new ProcurementProductSearchRequest
        {
            Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize,
            CustomerId = request.CustomerId, SupplierId = request.SupplierId
        };
        return await ExecuteQueryAsync(normalized, repository.SearchProductsAsync, cancellationToken);
    }

    /// <summary>读取客户历史筛选项；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, repository.SearchCustomerHistoryFiltersAsync, cancellationToken);

    /// <summary>读取供应商历史筛选项；权限和分页校验后进入同一只读事务。</summary>
    public Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default)
        => ExecuteAsync(request, repository.SearchSupplierHistoryFiltersAsync, cancellationToken);

    /// <summary>拒绝匿名及无效输入；授权依据和分页读取保持一致，失败或取消由事务释放回滚。</summary>
    private async Task<PagedResponseBase<T>> ExecuteAsync<T>(PagedRequestBase request,
        Func<PagedRequestBase, CancellationToken, Task<PagedResponseBase<T>>> query, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        // 不修改调用方对象；空关键词允许浏览第一页，长输入不静默截断。
        var normalized = new PagedRequestBase
        {
            Keyword = request.Keyword?.Trim(), PageIndex = request.PageIndex, PageSize = request.PageSize
        };
        return await ExecuteQueryAsync(normalized, query, cancellationToken);
    }

    /// <summary>校验候选分页公共输入；产品上下文为空表示不读取对应关系，不代表无效请求。</summary>
    private static void ValidateRequest(PagedRequestBase request)
    {
        if (request is null || request.PageIndex < 1 || request.PageSize is < 1 or > 100
            || (long)(request.PageIndex - 1) * request.PageSize > int.MaxValue || request.Keyword?.Length > 100)
            throw new ArgumentException("页码须为正数，每页1至100项，关键词不超过100字，分页偏移不得溢出。", nameof(request));
    }

    /// <summary>确认当前登录身份后，在服务拥有的只读事务中执行已规范化查询；仓储不承担角色鉴权。</summary>
    private async Task<PagedResponseBase<T>> ExecuteQueryAsync<TRequest, T>(TRequest request,
        Func<TRequest, CancellationToken, Task<PagedResponseBase<T>>> query, CancellationToken cancellationToken)
        where TRequest : PagedRequestBase
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!user.IsAuthenticated || !user.UserId.HasValue || user.UserId == Guid.Empty)
            throw new UnauthorizedAccessException("需要真实有效的采购用户。");
        if (context.Session.Transaction is not null)
            throw new InvalidOperationException("候选查询须独立拥有只读事务。");
        using var transaction = context.Session.Connection.BeginTransaction(IsolationLevel.Serializable);
        context.Session.Transaction = transaction;
        try
        {
            var result = await query(request, cancellationToken);
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

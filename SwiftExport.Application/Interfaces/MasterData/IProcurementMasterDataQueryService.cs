using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.MasterData;

/// <summary>独立采购主数据查询服务；负责权限、查询输入和只读事务边界。</summary>
public interface IProcurementMasterDataQueryService
{
    /// <summary>读取客户新增候选的稳定分页；无权限抛 UnauthorizedAccessException，输入无效抛 ArgumentException。</summary>
    Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>读取供应商新增候选的稳定分页；无权限抛 UnauthorizedAccessException，输入无效抛 ArgumentException。</summary>
    Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>读取标准采购产品新增候选的稳定分页；无权限抛 UnauthorizedAccessException，输入无效抛 ArgumentException。</summary>
    Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>读取客户历史筛选项的稳定分页；无权限抛 UnauthorizedAccessException，输入无效抛 ArgumentException。</summary>
    Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);

    /// <summary>读取供应商历史筛选项的稳定分页；无权限抛 UnauthorizedAccessException，输入无效抛 ArgumentException。</summary>
    Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken = default);
}

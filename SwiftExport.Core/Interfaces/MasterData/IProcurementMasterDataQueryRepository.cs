using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Core.Interfaces.MasterData;

/// <summary>独立采购主数据查询端口；只返回用例所需字段，使用调用方事务。</summary>
public interface IProcurementMasterDataQueryRepository
{
    /// <summary>读取客户新增候选的稳定分页；查询不写主数据或业务单据。</summary>
    Task<PagedResponseBase<ProcurementCustomerCandidateDto>> SearchCustomersAsync(
        PagedRequestBase request, CancellationToken cancellationToken);

    /// <summary>读取供应商新增候选的稳定分页；查询不写主数据或业务单据。</summary>
    Task<PagedResponseBase<ProcurementSupplierCandidateDto>> SearchSuppliersAsync(
        PagedRequestBase request, CancellationToken cancellationToken);

    /// <summary>读取采购供货产品新增候选的稳定分页；查询不写主数据或业务单据。</summary>
    Task<PagedResponseBase<ProcurementProductCandidateDto>> SearchProductsAsync(
        ProcurementProductSearchRequest request, CancellationToken cancellationToken);

    /// <summary>读取客户历史筛选项的稳定分页；查询不写主数据或业务单据。</summary>
    Task<PagedResponseBase<ProcurementCustomerFilterDto>> SearchCustomerHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken);

    /// <summary>读取供应商历史筛选项的稳定分页；查询不写主数据或业务单据。</summary>
    Task<PagedResponseBase<ProcurementSupplierFilterDto>> SearchSupplierHistoryFiltersAsync(
        PagedRequestBase request, CancellationToken cancellationToken);
}

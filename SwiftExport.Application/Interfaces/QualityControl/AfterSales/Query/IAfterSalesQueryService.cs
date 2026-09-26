using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Interfaces.QualityControl.AfterSales.Query;
/// <summary>AfterSalesQueryService 的窄应用契约。</summary>
public interface IAfterSalesQueryService
{
    /// <summary>查询单头分页。</summary>
    Task<ServiceResult<PagedResponseBase<AfterSalesListItemDto>>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken = default);
    /// <summary>读取头行一致性详情。</summary>
    Task<ServiceResult<AfterSalesDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default);
    /// <summary>读取安全候选。</summary>
    Task<ServiceResult<PagedResponseBase<AfterSalesLookupDto>>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken = default);
}

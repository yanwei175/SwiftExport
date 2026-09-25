using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.QualityControl.AfterSales.Lookup;
/// <summary>售后安全候选及同事务引用核验。</summary>
public interface IAfterSalesLookupRepository
{
    /// <summary>查询白名单候选。</summary>
    Task<PagedResponseBase<AfterSalesLookupDto>> LookupAsync(string kind, AfterSalesLookupRequest request, CancellationToken cancellationToken);
    /// <summary>在当前事务中核验引用并读取快照。</summary>
    Task<AfterSalesLookupDto?> ReferenceAsync(string kind, Guid uid, Guid? customerId, bool requireActive);
}

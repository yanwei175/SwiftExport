using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Core.Interfaces.QualityControl.AfterSales.Query;
/// <summary>售后列表只读投影。</summary>
public interface IAfterSalesQueryRepository
{
    /// <summary>按原筛选条件分页查询售后单。</summary>
    Task<PagedResponseBase<AfterSalesListItemDto>> SearchAsync(AfterSalesSearchRequest request, CancellationToken cancellationToken);
}

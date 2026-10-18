using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Suppliers.SupplierProducts.Query;

/// <summary>读取供应商产品资料，包括未关联公司产品的记录。</summary>
public interface ISupplierProductQueryService
{
    /// <summary>按资料身份读取详情。</summary>
    Task<ServiceResult<SupplierProductDto>> GetByIdAsync(Guid uid);
    /// <summary>按供应商分页读取资料。</summary>
    Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync(SupplierProductSearchRequest request, CancellationToken cancellationToken = default);
}

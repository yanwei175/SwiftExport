using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Customers.CustomerProducts.Query;

/// <summary>读取客户产品及其可选 Product 联表投影。</summary>
public interface ICustomerProductQueryService
{
    /// <summary>按身份读取详情。</summary>
    Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid uid);
    /// <summary>按客户料号和客户读取详情。</summary>
    Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, Guid customerId);
    /// <summary>读取客户产品分页。</summary>
    Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest request);
    /// <summary>读取有产品资料的客户。</summary>
    Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync();
}

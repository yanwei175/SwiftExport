using SwiftExport.Core.Interfaces.Customers.Management;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Core.Interfaces.Customers.Management
{
    /// <summary>客户资料持久化和管理/业务查询的仓储契约。</summary>
    public interface ICustomerRepository : IRepository<Customer>
    {
        Task<Customer?> GetByCustomerCodeAsync(string customerCode);
        Task<bool> ExistByCustomerCode(string CustomerCode);
        Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<string> CustomerCodeList);

        /// <summary>可取消地读取全部客户管理资料，排除软删除，保留停用记录，按编码和身份稳定排序。</summary>
        Task<IReadOnlyList<CustomerDto>> GetAllForManagementAsync(CancellationToken cancellationToken = default);

        Task<PagedResponseBase<CustomerDto>> GetPagedAsync(CustomerSearchRequest request);
        Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items);
        Task<PagedResponseBase<SalesOrderCustomerLookupDto>> SearchSalesOrderCustomersAsync(
            SalesOrderCustomerSearchRequest request,
            CancellationToken cancellationToken = default);
        Task<List<string>> GetSalesOrderCustomerCodesAsync(CancellationToken cancellationToken = default);

    }
}

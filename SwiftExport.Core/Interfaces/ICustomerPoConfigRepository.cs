using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomerPoConfigRepository : IRepository<CustomerPoConfig>
    {
        Task<List<string>> GetUsedByCustomerCodeAndSupplierCodeAsync(IEnumerable<PoConfigCheck> items);
        Task<IReadOnlyList<string>> GetExistingCustomerCodesAsync(IEnumerable<PoConfigCheck> items);
        /// <summary>
        /// 根据客户代码+供应商代码,获取非唯一项
        /// </summary>
        /// <param name="poConfigCheckList">PoConfigCheck</param>
        /// <returns>PoConfigCheck</returns>
        Task<IReadOnlyList<PoConfigCheck>> GetNonUniqueByPoConfigCheckList(IEnumerable<PoConfigCheck> poConfigCheckList);

        Task<PagedResponseBase<CustomerPoConfigDto>> GetPagedAsync(CustomerPoConfigSearchRequest request);

        /// <summary>按唯一业务编码解析并写入权威客户、供应商 Uid；无法唯一解析时返回 false。</summary>
        Task<bool> PopulateRelationIdsAsync(CustomerPoConfig entity);

    }
}

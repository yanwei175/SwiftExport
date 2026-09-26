using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces
{
    public interface ICustomerPoConfigRepository : IRepository<CustomerPoConfig>
    {
        /// <summary>按客户与供应商标识读取完整的活动配置 DTO，不跟踪或修改实体。</summary>
        Task<CustomerPoConfigDto?> GetByPartiesAsync(
            Guid customerId,
            Guid supplierId,
            CancellationToken cancellationToken = default,
            bool withUpdLock = false);

        /// <summary>查找被其他活动记录占用的客户与供应商组合；空 Uid 表示新增记录。</summary>
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

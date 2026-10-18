using SwiftExport.Core.Interfaces.Customers.PoConfigs;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces.Customers.PoConfigs
{
    /// <summary>读取和维护双方身份范围内的客户采购默认配置，旧编码查询仅供既有消费者兼容。</summary>
    public interface ICustomerPoConfigRepository : IRepository<CustomerPoConfig>
    {
        /// <summary>在调用方低隔离事务中锁定配置写入范围，保护不存在的双方组合，不自行提交事务。</summary>
        Task LockForWriteAsync(CancellationToken cancellationToken = default);
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

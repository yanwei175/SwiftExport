using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Core.Interfaces;

public interface ICustomerProductRepository : IRepository<CustomerProduct>
{
    // ============================================================
    // 读操作（联表查询，直接返回 DTO）
    // ============================================================

    /// <summary>
    /// 按 ID 联表查询单条，返回含联表字段的完整 DTO。
    /// 隐藏基类 GetByIdAsync（基类只查单表，联表字段为空）。
    /// </summary>
    Task<CustomerProductDto?> GetDtoByIdAsync(Guid id);

    /// <summary>
    /// 联表分页查询（CustomerProduct JOIN Product JOIN Customer JOIN ProductCategory）
    /// 直接返回 CustomerProductDto，无需中间读模型。
    /// criteria 字段说明：
    ///   - 普通字段（CustomerId / CustomerSku / Sku 等）→ 映射到 cp.字段
    ///   - CategoryId → 映射到 p.CategoryId
    /// </summary>
    Task<PagedResponseBase<CustomerProductDto>> GetPagedAsync(CustomerProductSearchRequest request);

    // ============================================================
    // 其他特有查询
    // ============================================================

    /// <summary>获取所有有客户产品记录的 CustomerId 列表（去重）</summary>
    Task<IReadOnlyList<int>> GetAllCustomerIdListAsync();

    /// <summary>按客户料号 + 客户 ID 精确查找实体（用于写操作前的存在性校验）</summary>
    Task<CustomerProduct?> GetByCustomerSkuAsync(string customerSku, Guid customerId);

    /// <summary>批量检查客户料号是否已存在（用于新增前去重校验）</summary>
    Task<IReadOnlyList<string>> GetExistingCustomerSkuListAsync(IEnumerable<string> skuList, Guid customerId);
    Task<List<string>> GetCustomerCodesUsedByOtherCustomersAsync(IEnumerable<(Guid Uid, string CustomerCode)> items);

}


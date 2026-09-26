using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 客户产品服务接口。
/// DTO 含联表字段；基础 CRUD 继承 IBaseService，联表分页和批量保存显式定义。
/// </summary>
public interface ICustomerProductService : IBaseService<CustomerProduct, CustomerProductDto>
{
    // ============================================================
    // 读操作（联表查询，直接返回 DTO）
    // ============================================================



    /// <summary>按客户料号 + 客户 ID 精确查询</summary>
    Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, Guid customerId);

    /// <summary>联表分页查询</summary>
    Task<ServiceResult<PagedResponseBase<CustomerProductDto>>> GetPagedAsync(CustomerProductSearchRequest req);

    /// <summary>获取所有有产品记录的客户列表</summary>
    Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync();

    // ============================================================
    // 写操作（只操作 CustomerProduct 实体，DTO 仅作为数据载体传入）
    // ============================================================

    /// <summary>批量保存（新增 / 修改 / 删除），事务保护</summary>
    Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(CustomerProductBatchSaveRequest batchReq);
}

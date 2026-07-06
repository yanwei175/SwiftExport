using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using System;

using SwiftExport.Contracts.DTOs.Customers.Requests;
using SwiftExport.Contracts.DTOs.Customers.Responses;
using SwiftExport.Contracts.DTOs.Products.Requests;
using SwiftExport.Contracts.DTOs.Products.Responses;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 客户产品服务接口。
/// 因 CustomerProduct 实体与 CustomerProductDto 不对称（DTO 含联表字段），
/// 故不继承 IBaseService，独立定义读写方法，职责边界清晰。
/// </summary>
public interface ICustomerProductService
{
    // ============================================================
    // 读操作（联表查询，直接返回 DTO）
    // ============================================================

    /// <summary>按 ID 联表查询单条客户产品</summary>
    Task<ServiceResult<CustomerProductDto>> GetByIdAsync(Guid id);

    /// <summary>按客户料号 + 客户 ID 精确查询</summary>
    Task<ServiceResult<CustomerProductDto>> GetByCustomerSkuAsync(string customerSku, Guid customerId);

    /// <summary>联表分页查询</summary>
    Task<ServiceResult<PagedResultDto<CustomerProductDto>>> GetPagedCustomerProductAsync(ProductSearchRequest req);

    /// <summary>获取所有有产品记录的客户列表</summary>
    Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllHaveProductCustomerListAsync();

    // ============================================================
    // 写操作（只操作 CustomerProduct 实体，DTO 仅作为数据载体传入）
    // ============================================================

    /// <summary>批量保存（新增 / 修改 / 删除），事务保护</summary>
    Task<ServiceResult<CustomerProductBatchSaveResponse>> BatchSaveAsync(CustomerProductBatchSaveRequest batchReq);
}
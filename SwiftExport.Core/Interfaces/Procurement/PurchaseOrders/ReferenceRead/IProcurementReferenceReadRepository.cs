using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.DTOs.MasterData.ProcurementLookups;

namespace SwiftExport.Core.Interfaces.Procurement;

/// <summary>在调用方事务内锁定客户、供应商、产品及单位等采购资格事实。</summary>
public interface IProcurementReferenceReadRepository
{
    /// <summary>读取并锁定当前客户资格。</summary>
    Task<Customer?> GetCustomerAsync(Guid uid, CancellationToken ct);
    /// <summary>读取并锁定当前供应商资格。</summary>
    Task<Supplier?> GetSupplierAsync(Guid uid, CancellationToken ct);
    /// <summary>读取并锁定当前产品身份与执行属性。</summary>
    Task<Product?> GetProductAsync(Guid uid, CancellationToken ct);
    /// <summary>读取创建采购行所需的权威产品及当前客户、供应商关系快照。</summary>
    Task<ProcurementProductCandidateDto?> GetProductCandidateAsync(Guid productId, Guid customerId, Guid supplierId, CancellationToken ct);
    /// <summary>取得唯一启用的规范单位代码，失效或重复时返回空。</summary>
    Task<string?> GetUnitAsync(string unit, CancellationToken ct);
}

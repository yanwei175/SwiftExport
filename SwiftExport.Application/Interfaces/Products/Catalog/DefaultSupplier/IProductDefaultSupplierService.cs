using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces.Products.Catalog.DefaultSupplier;

/// <summary>将有效供应商产品资料设为指定 Product 的默认供应商。</summary>
public interface IProductDefaultSupplierService
{
    /// <summary>在单一事务中核对关联及版本并更新 Product 默认供应商。</summary>
    Task<ServiceResult<SetProductDefaultSupplierResult>> SetProductDefaultSupplierAsync(SetProductDefaultSupplierCommand command);
}

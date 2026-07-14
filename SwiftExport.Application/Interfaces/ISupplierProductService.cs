using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Relations;

namespace SwiftExport.Application.Interfaces;

public interface ISupplierProductService : IBaseService<SupplierProduct, SupplierProductDto>
{
    Task<ServiceResult<PagedResponseBase<SupplierProductDto>>> GetPagedAsync(SupplierProductSearchRequest request);
    Task<ServiceResult<SupplierProductBatchSaveResponse>> BatchSaveAsync(SupplierProductBatchSaveRequest request);
}

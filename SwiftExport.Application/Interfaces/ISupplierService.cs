using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces;

public partial interface ISupplierService : IBaseService<Supplier, SupplierDto>
{

    Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync();
    Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync(SupplierSearchRequest request);
    Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest batchReq);

}

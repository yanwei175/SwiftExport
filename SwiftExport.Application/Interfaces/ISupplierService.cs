using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Interfaces
{
    public interface ISupplierService : IBaseService<Supplier, SupplierDto>
    {
        Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestGeneric<SupplierDto> batchReq);
    }
}

using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;

namespace SwiftExport.Application.Interfaces
{
    public interface IProductColorService : IBaseService<ProductColor, ProductColorDto>
    {
        Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestGeneric<ProductColorDto> batchReq);

    }
}

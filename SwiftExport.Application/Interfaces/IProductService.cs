using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces;

public partial interface IProductService
{
    Task<ServiceResult<ProductDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(ProductDto dto);
    Task<ServiceResult<int>> UpdateAsync(ProductDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductDto> batchReq);

    Task<ServiceResult<IReadOnlyList<string>>> GetAllCustomerCodeListAsync();

    // 在这里扩展特有的业务方法
    Task<ServiceResult<ProductDto>> GetBySkuAsync(string sku);

    Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(ProductSearchRequest search);
}

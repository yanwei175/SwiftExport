using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Services.Products;

public partial class BomService
{
    public Task<ServiceResult<BomDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<BomDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<BomDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<int>> UpdateAsync(BomDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<BomDto> batchReq) => _crud.BatchSaveAsync(batchReq, isSoftDelete: false);
}

public partial class ProductService
{
    public Task<ServiceResult<ProductDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<PagedResultDto<ProductDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(ProductDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(ProductDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, ValidateBeforeUpdateAsync);
}

public partial class ProductCategoryService
{
    public Task<ServiceResult<ProductCategoryDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<ProductCategoryDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<ProductCategoryDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(ProductCategoryDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(ProductCategoryDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductCategoryDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

public partial class ProductColorService
{
    public Task<ServiceResult<ProductColorDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<ProductColorDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<ProductColorDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(ProductColorDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(ProductColorDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<ProductColorDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, isSoftDelete: false);
}

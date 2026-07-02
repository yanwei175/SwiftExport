using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Services.Suppliers;

public partial class SupplierService
{
    public Task<ServiceResult<SupplierDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SupplierDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SupplierDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(SupplierDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SupplierDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, isSoftDelete: false);
}

using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Services.Customers;

public partial class CustomerService
{
    public Task<ServiceResult<CustomerDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(CustomerDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(CustomerDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<CustomerDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, isSoftDelete: false);
}

public partial class CustomerPoConfigService
{
    public Task<ServiceResult<CustomerPoConfigDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(CustomerPoConfigDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(CustomerPoConfigDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<CustomerPoConfigDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, isSoftDelete: false);
}

using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Services.Systems;

public partial class SysDicCurrencyService
{
    public Task<ServiceResult<SysDicCurrencyDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SysDicCurrencyDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SysDicCurrencyDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SysDicCurrencyDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysDicCurrencyDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, ValidateBeforeUpdateAsync);
}

public partial class SysDicCurrencyExchangeService
{
    public Task<ServiceResult<SysDicCurrencyExchangeDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SysDicCurrencyExchangeDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(SysDicCurrencyExchangeDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysDicCurrencyExchangeDto> batchReq) => _crud.BatchSaveAsync(batchReq, ValidateBeforeAddAsync, ValidateBeforeUpdateAsync);
}

public partial class SysDictDataService
{
    public Task<ServiceResult<SysDictDataDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SysDictDataDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SysDictDataDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(SysDictDataDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysDictDataDto> batchReq) => _crud.BatchSaveAsync(batchReq);
    public Task<ServiceResult<IReadOnlyList<SysDictDataDto>>> GetByCodeAsync(string dictCode) => _crud.GetListAsync(new { DictCode = dictCode });
}

public partial class SysDictTypeService
{
    public Task<ServiceResult<SysDictTypeDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SysDictTypeDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SysDictTypeDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SysDictTypeDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(SysDictTypeDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysDictTypeDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

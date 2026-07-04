using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
namespace SwiftExport.Application.Interfaces;

/// <summary>
/// 货币汇率服务接口。
/// </summary>
public partial interface ISysDicCurrencyExchangeService
{
    Task<ServiceResult<SysDicCurrencyExchangeDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<SysDicCurrencyExchangeDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<SysDicCurrencyExchangeDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(SysDicCurrencyExchangeDto dto);
    Task<ServiceResult<int>> UpdateAsync(SysDicCurrencyExchangeDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SysDicCurrencyExchangeDto> batchReq);

}

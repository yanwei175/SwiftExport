using {{ProjectName}}.Contracts.DTOs.Sample;
using {{ProjectName}}.Contracts.Wrappers;
using {{ProjectName}}.Core.Entities.Sample;

namespace {{ProjectName}}.Application.Interfaces;

public interface ISampleItemService
{
    Task<ServiceResult<SampleItemDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<PagedResultDto<SampleItemDto>>> SearchPageAsync(PagedRequestDto request);
    Task<ServiceResult<Guid>> CreateAsync(SampleItemDto dto);
    Task<ServiceResult<int>> UpdateAsync(Guid uid, SampleItemDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SampleItemDto> request);
    Task<SampleItemDto?> GetByCodeAsync(string code);
}

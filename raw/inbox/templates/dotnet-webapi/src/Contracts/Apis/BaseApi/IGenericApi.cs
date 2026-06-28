using Refit;
using {{ProjectName}}.Contracts.Wrappers;

namespace {{ProjectName}}.Contracts.Apis.BaseApi;

public interface IGenericApi<TDto> where TDto : class
{
    [Get("/{id}")]
    Task<ServiceResult<TDto>> GetByIdAsync(Guid id);

    [Post("/page")]
    Task<ServiceResult<PagedResultDto<TDto>>> SearchPageAsync([Body] PagedRequestDto request);

    [Post("/")]
    Task<ServiceResult<Guid>> CreateAsync([Body] TDto request);

    [Put("/{id}")]
    Task<ServiceResult<int>> UpdateAsync(Guid id, [Body] TDto request);

    [Delete("/{id}")]
    Task<ServiceResult<int>> DeleteAsync(Guid id);

    [Post("/batch-save")]
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync([Body] BatchRequestDto<TDto> request);
}

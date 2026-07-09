using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Application.Interfaces;

public interface IBaseService<TEntity, TDto> where TEntity : class where TDto : class
{
    Task<ServiceResult<TDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<SaveItemResult>> AddAsync(CreateItemRequest<TDto> item);
    Task<ServiceResult<SaveItemResult>> UpdateAsync(UpdateItemRequest<TDto> item);
    Task<ServiceResult<SaveItemResult>> DeleteAsync(DeleteItemRequest item);
}

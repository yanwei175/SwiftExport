using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces
{
    public partial interface ICustomerPoConfigService
    {
    Task<ServiceResult<CustomerPoConfigDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<CustomerPoConfigDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<CustomerPoConfigDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(CustomerPoConfigDto dto);
    Task<ServiceResult<int>> UpdateAsync(CustomerPoConfigDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<CustomerPoConfigDto> batchReq);

    }
}

using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Interfaces
{
    public partial interface ICustomerService
    {
    Task<ServiceResult<CustomerDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<CustomerDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(CustomerDto dto);
    Task<ServiceResult<int>> UpdateAsync(CustomerDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<CustomerDto> batchReq);

        Task<bool> ExistByCustomerCode(string customerCode);
        Task<ServiceResult<IReadOnlyList<string>>> GetNonExistingCustomerCodesAsync(IEnumerable<string> customerCodes);
        Task<ServiceResult<CustomerDto>> GetByCustomerCodeAsync(string customerCode);
        Task<ServiceResult<PagedResultDto<CustomerDto>>> GetPagedAsync(PagedRequestDto request);
    }
}

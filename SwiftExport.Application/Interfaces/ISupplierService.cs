using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
namespace SwiftExport.Application.Interfaces;

public partial interface ISupplierService
{
    Task<ServiceResult<SupplierDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync();
    Task<ServiceResult<PagedResultDto<SupplierDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");
    Task<ServiceResult<Guid>> AddAsync(SupplierDto dto);
    Task<ServiceResult<int>> UpdateAsync(SupplierDto dto);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);
    Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest batchReq);

}

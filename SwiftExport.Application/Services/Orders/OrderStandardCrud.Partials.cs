using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.DTOs.PackingLists;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Services.Orders;

public partial class DependentDemandService
{
    public Task<ServiceResult<IndependentDemandDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<IndependentDemandDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(IndependentDemandDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(IndependentDemandDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<IndependentDemandDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

public partial class FollowUpService
{
    public Task<ServiceResult<FollowUpLogDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<FollowUpLogDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<FollowUpLogDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(FollowUpLogDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(FollowUpLogDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<FollowUpLogDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

public partial class IndependentDemandService
{
    public Task<ServiceResult<IndependentDemandDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<IndependentDemandDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<IndependentDemandDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(IndependentDemandDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(IndependentDemandDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<IndependentDemandDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

public partial class PackingListService
{
    public Task<ServiceResult<PackingListHeadDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<PackingListHeadDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<PackingListHeadDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(PackingListHeadDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(PackingListHeadDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<PackingListHeadDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

public partial class PurchaseOrderService
{
    public Task<ServiceResult<PurchaseOrderHeadDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<PurchaseOrderHeadDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<PurchaseOrderHeadDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(PurchaseOrderHeadDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(PurchaseOrderHeadDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<PurchaseOrderHeadDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

public partial class SalesOrderHeadService
{
    public Task<ServiceResult<SalesOrderHeaderDto>> GetByIdAsync(Guid uid) => _crud.GetByIdAsync(uid);
    public Task<ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>> GetAllAsync() => _crud.GetAllAsync();
    public Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC") => _crud.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
    public Task<ServiceResult<Guid>> AddAsync(SalesOrderHeaderDto dto) => _crud.AddAsync(dto);
    public Task<ServiceResult<int>> UpdateAsync(SalesOrderHeaderDto dto) => _crud.UpdateAsync(dto);
    public Task<ServiceResult<int>> DeleteAsync(Guid uid) => _crud.DeleteAsync(uid);
    public Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<SalesOrderHeaderDto> batchReq) => _crud.BatchSaveAsync(batchReq);
}

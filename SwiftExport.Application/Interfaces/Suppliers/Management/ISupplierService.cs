using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
namespace SwiftExport.Application.Interfaces.Suppliers.Management;

/// <summary>编排供应商管理快照、分页读取和资料维护。</summary>
public partial interface ISupplierService : IBaseService<Supplier, SupplierDto>
{

    /// <summary>读取管理范围的全量快照，取消透传至数据库。</summary>
    Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedAsync(SupplierSearchRequest request);
    Task<ServiceResult<SupplierBatchSaveResponse>> BatchSaveAsync(SupplierBatchSaveRequest batchReq);

}

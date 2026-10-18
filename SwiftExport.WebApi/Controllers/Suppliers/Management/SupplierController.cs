using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Suppliers.Management;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.DTOs.Suppliers.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.RequestProtection;
namespace SwiftExport.WebApi.Controllers.Suppliers.Management;

/// <summary>供应商管理 HTTP 边界，委托服务读取快照、分页和保存资料。</summary>
public class SupplierController(ISupplierService service) : BaseController<Supplier, SupplierDto>(service)
{

    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<SupplierDto>>> GetPagedList([FromBody] SupplierSearchRequest request)
        => await service.GetPagedAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<SupplierBatchSaveResponse>> BatchSave([FromBody] SupplierBatchSaveRequest req) => await service.BatchSaveAsync(req);


    /// <summary>读取完整管理快照，透传请求取消。</summary>
    [HttpGet("all")]
    public async Task<ServiceResult<IReadOnlyList<SupplierDto>>> GetAll(CancellationToken cancellationToken)
    {
        return await service.GetAllAsync(cancellationToken);
    }


}

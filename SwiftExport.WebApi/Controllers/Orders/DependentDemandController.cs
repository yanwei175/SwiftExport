using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.DependentDemands;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.Orders.Responses;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;
namespace SwiftExport.WebApi.Controllers.Orders;


public class DependentDemandController(IDependentDemandService service) : BaseController<DependentDemand, DependentDemandDto>(service)
{
    private const string LegacyWriteDisabled = "LEGACY_DEMAND_WRITE_DISABLED：旧净需求写链路已冻结，请使用净需求工作台。";

    [HttpPost]
    public override Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<DependentDemandDto> dto)
        => Task.FromResult(ServiceResult<SaveItemResult>.Fail(LegacyWriteDisabled));

    [HttpPut("{id}")]
    public override Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<DependentDemandDto> dto)
        => Task.FromResult(ServiceResult<SaveItemResult>.Fail(LegacyWriteDisabled));

    [HttpDelete("{id}")]
    public override Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id)
        => Task.FromResult(ServiceResult<SaveItemResult>.Fail(LegacyWriteDisabled));

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<DependentDemandBatchSaveResponse>> BatchSave([FromBody] DependentDemandBatchSaveRequest req)
        => Task.FromResult(ServiceResult<DependentDemandBatchSaveResponse>.Fail(LegacyWriteDisabled));

    /// <summary>
    /// 分页查询净需求。
    /// </summary>
    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<DependentDemandDto>>> GetPagedListAsync([FromBody] DependentDemandSearchRequest request)
    {
        return await service.GetPagedAsync(request);
    }


    /// <summary>
    /// 按选中需求池ID列表转采购。
    /// </summary>
    [HttpPost("convert-to-po/by-ids")]
    public Task<ServiceResult<List<string>>> CoverToPoByIdList([FromBody] List<Guid> req)
        => Task.FromResult(ServiceResult<List<string>>.Fail(LegacyWriteDisabled));

    /// <summary>
    /// CoverToPurchaseOrderBySourceTypeAndSourceNoAsync
    /// </summary>
    [HttpPost("convert-to-po/{sourceType}/{sourceNo}")]
    public Task<ServiceResult<List<string>>> CoverToPoBySourceTypeAndSourceNo(string sourceType, string sourceNo)
        => Task.FromResult(ServiceResult<List<string>>.Fail(LegacyWriteDisabled));

    /// <summary>
    /// 手工指定需求池行供应商并持久化。
    /// </summary>
    [HttpPost("{id}/set-supplier")]
    public Task<ServiceResult<bool>> UpdateSupplierAsync(Guid id, [FromQuery] string supplierCode, [FromQuery] string supplierName = "")
        => Task.FromResult(ServiceResult<bool>.Fail(LegacyWriteDisabled));




}

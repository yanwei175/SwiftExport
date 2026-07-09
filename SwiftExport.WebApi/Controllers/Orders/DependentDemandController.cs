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
    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public async Task<ServiceResult<DependentDemandBatchSaveResponse>> BatchSave([FromBody] DependentDemandBatchSaveRequest req) => await service.BatchSaveAsync(req);

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
    public async Task<ServiceResult<List<string>>> CoverToPoByIdList([FromBody] List<Guid> req)
    {
        return await service.CoverToPurchaseOrderByDependentIdListAsync(req);
    }

    /// <summary>
    /// CoverToPurchaseOrderBySourceTypeAndSourceNoAsync
    /// </summary>
    [HttpPost("convert-to-po/{sourceType}/{sourceNo}")]
    public async Task<ServiceResult<List<string>>> CoverToPoBySourceTypeAndSourceNo(string sourceType, string sourceNo)
    {
        sourceType = sourceType.Trim();
        sourceNo = sourceNo.Trim();

        if (string.IsNullOrWhiteSpace(sourceType))
            return ServiceResult<List<string>>.Fail("来源类型不能为空");

        if (string.IsNullOrWhiteSpace(sourceNo))
            return ServiceResult<List<string>>.Fail("来源单号不能为空");

        if (!DependSourceTypesEnum.SourceTypes.Any(x => x == sourceType))
        {
            return ServiceResult<List<string>>.Fail($"不支持的来源类型: {sourceType}");
        }

        return await service.CoverToPurchaseOrderBySourceTypeAndSourceNoAsync(sourceType, sourceNo);
    }

    /// <summary>
    /// 手工指定需求池行供应商并持久化。
    /// </summary>
    [HttpPost("{id}/set-supplier")]
    public async Task<ServiceResult<bool>> UpdateSupplierAsync(Guid id, [FromQuery] string supplierCode, [FromQuery] string supplierName = "")
    {
        return await service.UpdateSupplierAsync(id, supplierCode, supplierName);
    }




}

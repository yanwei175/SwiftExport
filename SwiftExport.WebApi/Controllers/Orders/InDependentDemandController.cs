using Microsoft.AspNetCore.Mvc;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Independent;
using SwiftExport.Contracts.DTOs.Independent.Requests;
using SwiftExport.Contracts.DTOs.Independent.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.WebApi.Controllers.Base;
using SwiftExport.WebApi.Filters;

namespace SwiftExport.WebApi.Controllers.Orders;


public class InDependentDemandController(IIndependentDemandServices service) : BaseController<IndependentDemand, IndependentDemandDto>(service)
{
    private const string LegacyWriteDisabled = "LEGACY_DEMAND_WRITE_DISABLED：旧毛需求写链路已冻结，请使用毛需求工作台。";

    [HttpPost]
    public override Task<ServiceResult<SaveItemResult>> Create([FromBody] CreateItemRequest<IndependentDemandDto> dto)
        => Task.FromResult(ServiceResult<SaveItemResult>.Fail(LegacyWriteDisabled));

    [HttpPut("{id}")]
    public override Task<ServiceResult<SaveItemResult>> Update(Guid id, [FromBody] UpdateItemRequest<IndependentDemandDto> dto)
        => Task.FromResult(ServiceResult<SaveItemResult>.Fail(LegacyWriteDisabled));

    [HttpDelete("{id}")]
    public override Task<ServiceResult<SaveItemResult>> Delete(DeleteItemRequest id)
        => Task.FromResult(ServiceResult<SaveItemResult>.Fail(LegacyWriteDisabled));

    /// <summary>
    /// 按选中需求池ID列表转采购。
    /// </summary>
    [HttpPost("convert-to-do/by-ids")]
    public Task<ServiceResult<List<IndependentDemandDto>>> CoverToDependByIdList([FromBody] List<Guid> req)
        => Task.FromResult(ServiceResult<List<IndependentDemandDto>>.Fail(LegacyWriteDisabled));

    /// <summary>
    /// 批量回退草稿（将选中需求池ID列表的毛需求状态回退到草稿，适用于误操作或需要重新拆解的场景）
    /// </summary>
    /// <param name="orderIds">请求回退Id列表</param>
    /// <returns>回退成功列表</returns>
    [HttpPost("batch-rollback")]
    public Task<ServiceResult<List<IndependentDemandDto>>> BatchRollBackAsync([FromBody] List<Guid> orderIds)
        => Task.FromResult(ServiceResult<List<IndependentDemandDto>>.Fail(LegacyWriteDisabled));
    [HttpPost("page")]
    public async Task<ServiceResult<PagedResponseBase<IndependentDemandDto>>> GetPagedAsync([FromBody] IndependSearchRequest request)
    => await service.GetPagedAsync(request);

    [HttpPost("batch-save")]
    [PreventDuplicateRequest(WindowSeconds = 5)]
    public Task<ServiceResult<IndependentDemandBatchSaveResponse>> BatchSave([FromBody] IndependentDemandBatchSaveRequest req)
        => Task.FromResult(ServiceResult<IndependentDemandBatchSaveResponse>.Fail(LegacyWriteDisabled));

    /// <summary>
    /// 按来源单据触发销售毛需求拆解。
    /// 说明：审批通过后的销售需求由该接口驱动拆解进入需求池。
    /// </summary>
    [HttpPost("decompose-by-source")]
    public Task<ServiceResult<List<IndependentDemandDto>>> ExecuteDecomposeBySourceAsync([FromBody] IndependentDemandDecomposeRequestDto request)
        => Task.FromResult(ServiceResult<List<IndependentDemandDto>>.Fail(LegacyWriteDisabled));

}

using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Responses;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 销售订单应用服务实现。
/// </summary>
public partial class SalesOrderHeadService(
    ISalesOrderHeadRepository headRepo,
    ISalesOrderLineRepository lineRepo,
    ICustomerRepository customerRepository,
    ISalesOrderLineService salesOrderLineService,
    IDocumentNoService documentNoService,
    IIndependentDemandRepository salesDemandRepository,
    ServiceBaseContext context)
    : BaseService<SalesOrderHeader, SalesOrderHeaderDto>(headRepo, context), ISalesOrderHeadService
{

    private readonly ILogger<SalesOrderHeadService> _logger = context.LoggerFactory.CreateLogger<SalesOrderHeadService>();







    #region 显式基础读写


    public async Task<ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>> GetAllAsync()
    {
        try
        {
            var entities = await headRepo.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>.Ok(entities.Adapt<IReadOnlyList<SalesOrderHeaderDto>>())
                : ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>.Fail(ex.Message);
        }
    }


    public async Task<ServiceResult<SalesOrderHeadBatchSaveResponse>> BatchSaveAsync(SalesOrderHeadBatchSaveRequest batchReq)
    {
        //_session.Transaction = _session.Connection.BeginTransaction();
        //var result = new SalesOrderHeadBatchSaveResponse();

        //var createList = batchReq.CreateItems?.Adapt<List<SalesOrderHeader>>();
        //var updateList = batchReq.UpdateItems?.Adapt<List<SalesOrderHeader>>();
        //var deleteIds = batchReq.DeleteItems;

        //try
        //{
        //    var errorMsg = new StringBuilder();

        //    if (updateList is { Count: > 0 })
        //    {
        //        var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

        //        var needUpdateUids = updateList.Select(x => x.Uid).ToList();
        //        var existingUids = await headRepo.GetExistingIdListAsync(needUpdateUids);
        //        var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
        //        if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
        //    }

        //    if (deleteIds is { Count: > 0 })
        //    {
        //        var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
        //    }

        //    if (errorMsg.Length > 0)
        //        return ServiceResult<SalesOrderHeadBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

        //    if (headRepo is not IBatchWriteRepository<SalesOrderHeader> batchRepository)
        //        return ServiceResult<SalesOrderHeadBatchSaveResponse>.Fail($"{nameof(SalesOrderHeader)} 仓储未实现批量保存");

        //    if (createList is { Count: > 0 })
        //    {
        //        result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
        //    }

        //    if (updateList is { Count: > 0 })
        //    {
        //        await batchRepository.UpdateRangeAsync(updateList);
        //        result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
        //    }

        //    if (deleteIds is { Count: > 0 })
        //    {
        //        await batchRepository.DeleteRangeSoftAsync(deleteIds);
        //        result.DeleteIdList = deleteIds;
        //    }

        //    _session.Transaction.Commit();
        //    return ServiceResult<SalesOrderHeadBatchSaveResponse>.Ok(result);
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<SalesOrderHeadBatchSaveResponse>.Fail(ex.Message);
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
        throw new NotImplementedException();
    }

    public Task<ServiceResult<PagedResponseBase<SalesOrderHeaderDto>>> GetPagedListAsync(SalesOrderSearchRequest request)
    {
        throw new NotImplementedException();
    }
    #endregion

}

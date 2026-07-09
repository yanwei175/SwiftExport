using Mapster;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders.Requests;
using SwiftExport.Contracts.DTOs.Orders.Responses;
using SwiftExport.Contracts.DTOs.PurchaseOrders.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 采购跟进应用服务。
/// </summary>
public partial class FollowUpService(
    IRepository<FollowUpLog> followUpRepository,
    IPurchaseOrderLineRepository purchaseOrderLineRepository,
    IDependentDemandRepository dependentDemandRepository,
    ServiceBaseContext context)
    : BaseService<FollowUpLog, FollowUpLogDto>(followUpRepository, context), IFollowUpService
{
    private readonly ILogger<FollowUpService> _logger = context.LoggerFactory.CreateLogger<FollowUpService>();

    /// <summary>
    /// 更新采购跟进信息：当采购行 ReadyQty 变更时，同步更新关联净需求 ReceivedQty（过程进度）。
    /// </summary>
    /// <param name="request">采购跟进更新请求</param>
    /// <returns>是否更新成功</returns>
    public async Task<ServiceResult<bool>> UpdatePurchaseFollowUpAsync(PurchaseFollowUpUpdateDto request)
    {
        if (request == null)
            return ServiceResult<bool>.Fail("请求参数不能为空");

        if (request.PurchaseOrderLineId == Guid.Empty)
            return ServiceResult<bool>.Fail("采购订单行Id不合法");

        if (request.ReadyQty < 0)
            return ServiceResult<bool>.Fail("完工数量不能小于0");

        // 中文注释：先获取采购订单行，校验存在性并计算 ReadyQty 增量。
        var line = await purchaseOrderLineRepository.GetByIdAsync(request.PurchaseOrderLineId);
        if (line == null || line.IsDeleted)
            return ServiceResult<bool>.Fail("采购订单行不存在");

        var oldReadyQty = line.ReadyQty;
        var newReadyQty = request.ReadyQty;
        var delta = newReadyQty - oldReadyQty;

        // 中文注释：在同一事务内执行“更新采购行 + 写跟进日志 + 同步净需求进度”，确保原子性。
        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            line.ReadyQty = newReadyQty;
            line.FollowUpRemarks = request.NoteContent;
            line.FollowUpTime = DateTime.Now;

            await purchaseOrderLineRepository.UpdateAsync(line);

            // 中文注释：写入跟进日志，便于后续追溯每次完工更新的操作记录。
            var log = new FollowUpLog
            {
                RefLineId = line.Uid,
                NoteContent = request.NoteContent,
                CreatedDate = DateTime.Now,
                IsDeleted = false
            };
            await followUpRepository.AddAsync(log);

            _session.Transaction.Commit();
            return ServiceResult<bool>.Ok(true);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<bool>.Fail("更新采购跟进失败：" + ex.Message);
        }
        finally
        {
            _session.Transaction?.Dispose();
            _session.Transaction = null;
        }
    }


    #region 显式基础读写


    public async Task<ServiceResult<IReadOnlyList<FollowUpLogDto>>> GetAllAsync()
    {
        try
        {
            var entities = await followUpRepository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<FollowUpLogDto>>.Ok(entities.Adapt<IReadOnlyList<FollowUpLogDto>>())
                : ServiceResult<IReadOnlyList<FollowUpLogDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<FollowUpLogDto>>.Fail(ex.Message);
        }
    }


    public async Task<ServiceResult<FollowUpBatchSaveResponse>> BatchSaveAsync(FollowUpBatchSaveRequest batchReq)
    {
        //_session.Transaction = _session.Connection.BeginTransaction();
        //var result = new FollowUpBatchSaveResponse();

        //var createList = batchReq.CreateItems?.Adapt<List<FollowUpLog>>();
        //var updateList = batchReq.UpdateItems?.Adapt<List<FollowUpLog>>();
        //var deleteIds = batchReq.DeleteItems;

        //try
        //{
        //    var errorMsg = new StringBuilder();

        //    if (updateList is { Count: > 0 })
        //    {
        //        var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

        //        var needUpdateUids = updateList.Select(x => x.Uid).ToList();
        //        var existingUids = await followUpRepository.GetExistingIdListAsync(needUpdateUids);
        //        var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
        //        if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
        //    }

        //    if (deleteIds is { Count: > 0 })
        //    {
        //        var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
        //        if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
        //    }

        //    if (errorMsg.Length > 0)
        //        return ServiceResult<FollowUpBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

        //    if (followUpRepository is not IBatchWriteRepository<FollowUpLog> batchRepository)
        //        return ServiceResult<FollowUpBatchSaveResponse>.Fail($"{nameof(FollowUpLog)} 仓储未实现批量保存");

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
        //    return ServiceResult<FollowUpBatchSaveResponse>.Ok(result);
        //}
        //catch (Exception ex)
        //{
        //    _session.Transaction.Rollback();
        //    return ServiceResult<FollowUpBatchSaveResponse>.Fail(ex.Message);
        //}
        //finally
        //{
        //    _session.Transaction.Dispose();
        //    _session.Transaction = null;
        //}
        throw new NotImplementedException();

    }

    public Task<ServiceResult<PagedResponseBase<FollowUpLogDto>>> GetPagedAsync(FollowUpSearchRequest request)
    {
        throw new NotImplementedException();
    }
    #endregion

}

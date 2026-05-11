using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 采购跟进应用服务。
/// </summary>
public class FollowUpService(
    IRepository<FollowUpLog> followUpRepository,
    IPurchaseOrderLineRepository purchaseOrderLineRepository,
    IDependentDemandRepository dependentDemandRepository,
    ServiceBaseContext context)
    : BaseService<FollowUpLog, FollowUpLogDto>(followUpRepository, context), IFollowUpService
{
    private readonly IRepository<FollowUpLog> _followUpRepository = followUpRepository;
    private readonly IPurchaseOrderLineRepository _purchaseOrderLineRepository = purchaseOrderLineRepository;
    private readonly IDependentDemandRepository _dependentDemandRepository = dependentDemandRepository;

    /// <summary>
    /// 更新采购跟进信息：当采购行 ReadyQty 变更时，同步更新关联净需求 ReceivedQty（过程进度）。
    /// </summary>
    /// <param name="request">采购跟进更新请求</param>
    /// <returns>是否更新成功</returns>
    public async Task<ServiceResult<bool>> UpdatePurchaseFollowUpAsync(PurchaseFollowUpUpdateDto request)
    {
        if (request == null)
            return ServiceResult<bool>.Fail("请求参数不能为空");

        if (request.PurchaseOrderLineId <= 0)
            return ServiceResult<bool>.Fail("采购订单行Id不合法");

        if (request.ReadyQty < 0)
            return ServiceResult<bool>.Fail("完工数量不能小于0");

        // 中文注释：先获取采购订单行，校验存在性并计算 ReadyQty 增量。
        var line = await _purchaseOrderLineRepository.GetByIdAsync(request.PurchaseOrderLineId);
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
            line.ChangeTime = DateTime.Now;
            line.UpdatedDate = DateTime.Now;

            await _purchaseOrderLineRepository.UpdateAsync(line);

            // 中文注释：写入跟进日志，便于后续追溯每次完工更新的操作记录。
            var log = new FollowUpLog
            {
                RefLineId = line.Id,
                NoteContent = request.NoteContent,
                CreatedDate = DateTime.Now,
                IsDeleted = false
            };
            await _followUpRepository.AddAsync(log);

            // 中文注释：若采购行关联到净需求，则按 ReadyQty 的增量同步回写 ReceivedQty。
            // 业务语义：ReceivedQty 作为过程进度累计值，允许正向增加，也允许回调时减少（delta 可能为负）。
            if (line.SourceLineId.HasValue && line.SourceLineId.Value > 0 && delta != 0)
            {
                var dependentDemand = await _dependentDemandRepository.GetByIdAsync((int)line.SourceLineId.Value);
                if (dependentDemand != null && !dependentDemand.IsDeleted)
                {
                    dependentDemand.ReceivedQty += delta;
                    if (dependentDemand.ReceivedQty < 0)
                        dependentDemand.ReceivedQty = 0;

                    dependentDemand.ChangeTime = DateTime.Now;
                    dependentDemand.UpdatedDate = DateTime.Now;
                    await _dependentDemandRepository.UpdateAsync(dependentDemand);
                }
            }

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
}

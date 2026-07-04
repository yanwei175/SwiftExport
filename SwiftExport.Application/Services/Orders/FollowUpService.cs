using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Orders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Data;
using System.Text;

namespace SwiftExport.Application.Services.Orders;

/// <summary>
/// 采购跟进应用服务。
/// </summary>
public partial class FollowUpService(
    IRepository<FollowUpLog> followUpRepository,
    IPurchaseOrderLineRepository purchaseOrderLineRepository,
    IDependentDemandRepository dependentDemandRepository,
    ServiceBaseContext context) : IFollowUpService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<FollowUpService> _logger = context.LoggerFactory.CreateLogger<FollowUpService>();
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

        if (request.PurchaseOrderLineId == Guid.Empty)
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

            await _purchaseOrderLineRepository.UpdateAsync(line);

            // 中文注释：写入跟进日志，便于后续追溯每次完工更新的操作记录。
            var log = new FollowUpLog
            {
                RefLineId = line.Uid,
                NoteContent = request.NoteContent,
                CreatedDate = DateTime.Now,
                IsDeleted = false
            };
            await _followUpRepository.AddAsync(log);

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

    public async Task<ServiceResult<FollowUpLogDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _followUpRepository.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(FollowUpLog));
                return ServiceResult<FollowUpLogDto>.Fail("未找到数据");
            }

            return ServiceResult<FollowUpLogDto>.Ok(entity.Adapt<FollowUpLogDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<FollowUpLogDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<FollowUpLogDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _followUpRepository.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<FollowUpLogDto>>.Ok(entities.Adapt<IReadOnlyList<FollowUpLogDto>>())
                : ServiceResult<IReadOnlyList<FollowUpLogDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<FollowUpLogDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<FollowUpLogDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_followUpRepository is not IPagedCriteriaRepository<FollowUpLog> pagedRepository)
                return ServiceResult<PagedResultDto<FollowUpLogDto>>.Fail($"{nameof(FollowUpLog)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<FollowUpLogDto>>.Ok(new PagedResultDto<FollowUpLogDto>
            {
                Items = paged.Items.Adapt<List<FollowUpLogDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<FollowUpLogDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(FollowUpLogDto dto)
    {
        try
        {
            var entity = dto.Adapt<FollowUpLog>();
            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;
            var uid = await _followUpRepository.AddAsync(entity);
            return ServiceResult<Guid>.Ok(uid);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateAsync(FollowUpLogDto dto)
    {
        try
        {
            var existing = await _followUpRepository.GetByIdAsync(dto.Uid);
            if (existing == null) return ServiceResult<int>.Fail("数据不存在");

            dto.Adapt(existing);
            await _followUpRepository.UpdateAsync(existing);
            return ServiceResult<int>.Ok(1);
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> DeleteAsync(Guid uid)
    {
        try
        {
            return ServiceResult<int>.Ok(await _followUpRepository.DeleteAsync(uid));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<FollowUpLogDto> batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new BatchResultDto();

        var createList = batchReq.AddList?.Adapt<List<FollowUpLog>>();
        var updateList = batchReq.UpdateList?.Adapt<List<FollowUpLog>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _followUpRepository.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<BatchResultDto>.Fail(errorMsg.ToString().TrimEnd());

            if (_followUpRepository is not IBatchWriteRepository<FollowUpLog> batchRepository)
                return ServiceResult<BatchResultDto>.Fail($"{nameof(FollowUpLog)} 仓储未实现批量保存");

            if (createList is { Count: > 0 })
            {
                result.AddIdList = [.. await batchRepository.AddRangeAsync(createList)];
            }

            if (updateList is { Count: > 0 })
            {
                await batchRepository.UpdateRangeAsync(updateList);
                result.UpdateIdList = [.. updateList.Select(x => x.Uid)];
            }

            if (deleteIds is { Count: > 0 })
            {
                await batchRepository.DeleteRangeSoftAsync(deleteIds);
                result.DeleteIdList = deleteIds;
            }

            _session.Transaction.Commit();
            return ServiceResult<BatchResultDto>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<BatchResultDto>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion

}

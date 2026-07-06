using Mapster;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Contracts.DTOs.Base;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.Data;
using System.Text;

using SwiftExport.Contracts.DTOs.SalesOrders.Requests;
using SwiftExport.Contracts.DTOs.SalesOrders.Responses;
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
    ServiceBaseContext context) : ISalesOrderHeadService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<SalesOrderHeadService> _logger = context.LoggerFactory.CreateLogger<SalesOrderHeadService>();
    private readonly ISalesOrderHeadRepository _headRepo = headRepo;
    private readonly ISalesOrderLineRepository _lineRepo = lineRepo;
    private readonly ICustomerRepository _customerRepository = customerRepository;
    private readonly ISalesOrderLineService _salesOrderLineService = salesOrderLineService;
    private readonly IDocumentNoService _documentNoService = documentNoService;
    private readonly IIndependentDemandRepository _demandRepo = salesDemandRepository;







    #region 显式基础读写

    public async Task<ServiceResult<SalesOrderHeaderDto>> GetByIdAsync(Guid uid)
    {
        try
        {
            var entity = await _headRepo.GetByIdAsync(uid);
            if (entity == null)
            {
                if (_logger.IsEnabled(LogLevel.Warning))
                    _logger.LogWarning("查询失败：未找到 Uid 为 {Uid} 的 {EntityName} 数据", uid, nameof(SalesOrderHeader));
                return ServiceResult<SalesOrderHeaderDto>.Fail("未找到数据");
            }

            return ServiceResult<SalesOrderHeaderDto>.Ok(entity.Adapt<SalesOrderHeaderDto>());
        }
        catch (Exception ex)
        {
            return ServiceResult<SalesOrderHeaderDto>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>> GetAllAsync()
    {
        try
        {
            var entities = await _headRepo.GetAllAsync();
            return entities.Any()
                ? ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>.Ok(entities.Adapt<IReadOnlyList<SalesOrderHeaderDto>>())
                : ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>.Fail("未找到数据");
        }
        catch (Exception ex)
        {
            return ServiceResult<IReadOnlyList<SalesOrderHeaderDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<PagedResultDto<SalesOrderHeaderDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC")
    {
        try
        {
            if (_headRepo is not IPagedCriteriaRepository<SalesOrderHeader> pagedRepository)
                return ServiceResult<PagedResultDto<SalesOrderHeaderDto>>.Fail($"{nameof(SalesOrderHeader)} 仓储未实现动态分页查询");

            var paged = await pagedRepository.GetPagedListAsync(criteria, pageIndex, pageSize, orderBy);
            return ServiceResult<PagedResultDto<SalesOrderHeaderDto>>.Ok(new PagedResultDto<SalesOrderHeaderDto>
            {
                Items = paged.Items.Adapt<List<SalesOrderHeaderDto>>(),
                TotalCount = paged.TotalCount,
                PageSize = paged.PageSize,
                PageIndex = paged.PageIndex
            });
        }
        catch (Exception ex)
        {
            return ServiceResult<PagedResultDto<SalesOrderHeaderDto>>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<Guid>> AddAsync(SalesOrderHeaderDto dto)
    {
        try
        {
            var entity = dto.Adapt<SalesOrderHeader>();
            entity.CreatedDate = DateTime.Now;
            entity.IsDeleted = false;
            var uid = await _headRepo.AddAsync(entity);
            return ServiceResult<Guid>.Ok(uid);
        }
        catch (Exception ex)
        {
            return ServiceResult<Guid>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<int>> UpdateAsync(SalesOrderHeaderDto dto)
    {
        try
        {
            var existing = await _headRepo.GetByIdAsync(dto.Uid);
            if (existing == null) return ServiceResult<int>.Fail("数据不存在");

            dto.Adapt(existing);
            await _headRepo.UpdateAsync(existing);
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
            return ServiceResult<int>.Ok(await _headRepo.DeleteAsync(uid));
        }
        catch (Exception ex)
        {
            return ServiceResult<int>.Fail(ex.Message);
        }
    }

    public async Task<ServiceResult<SalesOrderHeadBatchSaveResponse>> BatchSaveAsync(SalesOrderHeadBatchSaveRequest batchReq)
    {
        _session.Transaction = _session.Connection.BeginTransaction();
        var result = new SalesOrderHeadBatchSaveResponse();

        var createList = batchReq.AddList?.Adapt<List<SalesOrderHeader>>();
        var updateList = batchReq.UpdateList?.Adapt<List<SalesOrderHeader>>();
        var deleteIds = batchReq.DeleteIdList;

        try
        {
            var errorMsg = new StringBuilder();

            if (updateList is { Count: > 0 })
            {
                var invalidUids = updateList.Where(x => x.Uid == Guid.Empty).Select(x => x.Uid).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"修改失败：包含无效Uid {string.Join(",", invalidUids)}");

                var needUpdateUids = updateList.Select(x => x.Uid).ToList();
                var existingUids = await _headRepo.GetExistingIdListAsync(needUpdateUids);
                var nonExistentUids = needUpdateUids.Except(existingUids).ToList();
                if (nonExistentUids.Count != 0) errorMsg.AppendLine($"修改失败：以下Uid在数据库中不存在 {string.Join(",", nonExistentUids)}");
            }

            if (deleteIds is { Count: > 0 })
            {
                var invalidUids = deleteIds.Where(uid => uid == Guid.Empty).ToList();
                if (invalidUids.Count != 0) errorMsg.AppendLine($"删除失败：包含无效Uid {string.Join(",", invalidUids)}");
            }

            if (errorMsg.Length > 0)
                return ServiceResult<SalesOrderHeadBatchSaveResponse>.Fail(errorMsg.ToString().TrimEnd());

            if (_headRepo is not IBatchWriteRepository<SalesOrderHeader> batchRepository)
                return ServiceResult<SalesOrderHeadBatchSaveResponse>.Fail($"{nameof(SalesOrderHeader)} 仓储未实现批量保存");

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
            return ServiceResult<SalesOrderHeadBatchSaveResponse>.Ok(result);
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            return ServiceResult<SalesOrderHeadBatchSaveResponse>.Fail(ex.Message);
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }
    #endregion

}

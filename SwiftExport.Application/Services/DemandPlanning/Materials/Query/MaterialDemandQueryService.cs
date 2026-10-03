using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.Trace;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Query;

/// <summary>提供物料需求工作台和来源导航的只读查询。</summary>
public sealed class MaterialDemandQueryService(ServiceBaseContext context, IMaterialDemandQueryRepository netDemandRepository) : IMaterialDemandQueryService
{
    private readonly ILogger<MaterialDemandQueryService> _logger = context.LoggerFactory.CreateLogger<MaterialDemandQueryService>();

    /// <summary>按稳定物料行身份读取追踪，保留历史修订及异常关系。</summary>
    public async Task<ServiceResult<MaterialDemandTraceDetailDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<MaterialDemandTraceDetailDto>.Fail("物料需求 Uid 不能为空。");
        try
        {
            var detail = await netDemandRepository.GetTraceAsync(uid, cancellationToken);
            return detail is null ? ServiceResult<MaterialDemandTraceDetailDto>.Fail("未找到物料需求。")
                : ServiceResult<MaterialDemandTraceDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (context.Session.Errors.Classify(ex) == DatabaseFailureKind.UnverifiedReceiptHistory)
        {
            _logger.LogWarning(ex, "物料需求追踪的正式入库反向历史尚未核实，物料需求 {NetDemandId}", uid);
            return ServiceResult<MaterialDemandTraceDetailDto>.Fail("正式入库反向历史需核实，收货量未知。");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求追踪失败，物料需求 {NetDemandId}", uid);
            return ServiceResult<MaterialDemandTraceDetailDto>.Fail("查询物料需求追踪失败。");
        }
    }

    /// <summary>查询物料需求工作台的物料事实和有效采购关联汇总。</summary>
    public async Task<ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>> SearchManagerAsync(
        MaterialDemandManagerSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new MaterialDemandManagerSearchRequest();
            request.PageIndex = Math.Max(1, request.PageIndex);
            request.PageSize = Math.Clamp(request.PageSize, 1, 200);
            return ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Ok(
                await netDemandRepository.SearchManagerAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求工作台失败。");
            return ServiceResult<PagedResponseBase<MaterialDemandManagerItemDto>>.Fail("查询物料需求失败。");
        }
    }

    /// <summary>查询不含计划状态的物料需求来源导航。</summary>
    public async Task<ServiceResult<List<NetDemandSourceGroupDto>>> SearchMaterialSourceGroupsAsync(
        MaterialDemandSourceGroupSearchRequest request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new MaterialDemandSourceGroupSearchRequest();
            var groups = await netDemandRepository.SearchMaterialSourceGroupsAsync(request, cancellationToken);
            return ServiceResult<List<NetDemandSourceGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询物料需求来源导航失败。");
            return ServiceResult<List<NetDemandSourceGroupDto>>.Fail("查询物料需求来源导航失败。");
        }
    }

}

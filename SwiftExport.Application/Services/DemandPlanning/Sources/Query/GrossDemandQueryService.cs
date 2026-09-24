using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Query;

/// <summary>提供来源需求本体的列表、分组和详情只读查询。</summary>
public sealed class GrossDemandQueryService(ServiceBaseContext context, IGrossDemandRepository grossDemandRepository) : IGrossDemandQueryService
{
    private readonly ILogger<GrossDemandQueryService> _logger = context.LoggerFactory.CreateLogger<GrossDemandQueryService>();

    /// <summary>查询来源需求本体分页，不投影物料或执行进度。</summary>
    public async Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new GrossDemandSearchRequest();
            if (request.SourceUid.HasValue) request.PageIndex = 1;
            else
            {
                request.PageIndex = Math.Max(1, request.PageIndex);
                request.PageSize = Math.Clamp(request.PageSize, 1, 200);
            }
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(await grossDemandRepository.SearchWorkspaceAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源需求失败。");
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail("查询来源需求失败。");
        }
    }

    /// <summary>查询来源单导航分组，筛选口径只属于来源需求本体。</summary>
    public async Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            request ??= new GrossDemandSourceGroupSearchRequest();
            return ServiceResult<List<GrossDemandSourceGroupDto>>.Ok([.. await grossDemandRepository.SearchSourceGroupsAsync(request, cancellationToken)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询来源需求分组失败。");
            return ServiceResult<List<GrossDemandSourceGroupDto>>.Fail("查询来源需求分组失败。");
        }
    }

    /// <summary>读取单条来源需求本体详情。</summary>
    public async Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<GrossDemandDetailDto>.Fail("来源需求 Uid 不能为空。");
        try
        {
            var detail = await grossDemandRepository.GetWorkspaceDetailAsync(uid, cancellationToken);
            return detail is null ? ServiceResult<GrossDemandDetailDto>.Fail("未找到来源需求。") : ServiceResult<GrossDemandDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载来源需求 {Uid} 失败。", uid);
            return ServiceResult<GrossDemandDetailDto>.Fail("加载来源需求失败。");
        }
    }
}

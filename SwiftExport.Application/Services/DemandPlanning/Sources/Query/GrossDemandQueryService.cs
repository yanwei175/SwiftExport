using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Query;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Application.Interfaces.DemandPlanning.Sources.Query;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.SysEnums;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Query;

/// <summary>提供成品需求本体的列表、分组和详情只读查询。</summary>
public sealed class GrossDemandQueryService(ServiceBaseContext context, IGrossDemandQueryRepository grossDemandRepository) : IGrossDemandQueryService
{
    private readonly ILogger<GrossDemandQueryService> _logger = context.LoggerFactory.CreateLogger<GrossDemandQueryService>();

    /// <summary>独立读取全范围客户候选；失败保持失败语义，不伪装为成功空列表。</summary>
    public async Task<ServiceResult<List<GrossDemandCustomerOptionDto>>> GetCustomerOptionsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try { return ServiceResult<List<GrossDemandCustomerOptionDto>>.Ok(await grossDemandRepository.GetCustomerOptionsAsync(cancellationToken)); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex,"加载成品需求客户候选失败。");
            return ServiceResult<List<GrossDemandCustomerOptionDto>>.Fail(ServiceErrorCodes.ServiceUnavailable,"加载成品需求客户候选失败，请稍后重试。");
        }
    }

    /// <summary>查询成品需求本体分页，不投影物料或执行进度。</summary>
    public async Task<ServiceResult<PagedResponseBase<GrossDemandListItemDto>>> SearchAsync(GrossDemandSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request ??= new GrossDemandSearchRequest();
        if (request.SourceUid == Guid.Empty)
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail(ServiceErrorCodes.Validation,"选定来源身份不能为空。",nameof(request.SourceUid));
        if (request.SourceTypes?.Any(value => value is not ("SO" or "CR" or "MD")) == true)
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail(ServiceErrorCodes.Validation,"来源类型仅支持 SO、CR、MD。",nameof(request.SourceTypes));
        if (request.LifecycleStatuses?.Any(value => !Enum.IsDefined(value)) == true)
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail(ServiceErrorCodes.Validation,"需求生命周期仅支持活动、已关闭和已作废。",nameof(request.LifecycleStatuses));
        if (request.CustomerIds?.Any(value => value == Guid.Empty) == true)
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail(ServiceErrorCodes.Validation,"客户身份不能为空。",nameof(request.CustomerIds));
        if (request.RequiredDateFrom?.Date > request.RequiredDateTo?.Date)
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail(ServiceErrorCodes.Validation,"开始交期不能晚于结束交期。",nameof(request.RequiredDateTo));
        try
        {
            // 分页规范化由仓储处理，服务不回写已应用查询条件。
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Ok(await grossDemandRepository.SearchWorkspaceAsync(request, cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询成品需求失败。");
            return ServiceResult<PagedResponseBase<GrossDemandListItemDto>>.Fail("查询成品需求失败。");
        }
    }

    /// <summary>查询来源单导航分组，筛选口径只属于成品需求本体。</summary>
    public async Task<ServiceResult<List<GrossDemandSourceGroupDto>>> SearchSourceGroupsAsync(GrossDemandSourceGroupSearchRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        request ??= new GrossDemandSourceGroupSearchRequest();
        if (request.LifecycleStatuses?.Any(value => !Enum.IsDefined(value)) == true)
            return ServiceResult<List<GrossDemandSourceGroupDto>>.Fail(ServiceErrorCodes.Validation,"需求生命周期仅支持活动、已关闭和已作废。",nameof(request.LifecycleStatuses));
        try
        {
            return ServiceResult<List<GrossDemandSourceGroupDto>>.Ok([.. await grossDemandRepository.SearchSourceGroupsAsync(request, cancellationToken)]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询成品需求分组失败。");
            return ServiceResult<List<GrossDemandSourceGroupDto>>.Fail("查询成品需求分组失败。");
        }
    }

    /// <summary>读取单条成品需求本体详情。</summary>
    public async Task<ServiceResult<GrossDemandDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty) return ServiceResult<GrossDemandDetailDto>.Fail("成品需求 Uid 不能为空。");
        try
        {
            var detail = await grossDemandRepository.GetWorkspaceDetailAsync(uid, cancellationToken);
            return detail is null ? ServiceResult<GrossDemandDetailDto>.Fail("未找到成品需求。") : ServiceResult<GrossDemandDetailDto>.Ok(detail);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "加载成品需求 {Uid} 失败。", uid);
            return ServiceResult<GrossDemandDetailDto>.Fail("加载成品需求失败。");
        }
    }

}

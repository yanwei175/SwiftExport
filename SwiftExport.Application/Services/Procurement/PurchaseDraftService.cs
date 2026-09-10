using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.DemandPlanning;

namespace SwiftExport.Application.Services.Procurement;

/// <summary>统一承接采购候选读取、草稿预览创建及生命周期，不经由物料工作台服务转发。</summary>
public sealed class PurchaseDraftService(
    ServiceBaseContext context,
    INetDemandRepository materialDemandFacts,
    PurchaseDraftCreationService creation,
    PurchaseDraftLifecycleService lifecycle) : IPurchaseDraftService
{
    private readonly ILogger<PurchaseDraftService> _logger = context.LoggerFactory.CreateLogger<PurchaseDraftService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>> GetCandidateSourceGroupsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var groups = await materialDemandFacts.GetPurchaseCandidateSourceGroupsAsync(cancellationToken);
            return ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Ok([.. groups]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询采购候选来源失败。");
            return ServiceResult<List<PurchaseDraftCandidateSourceGroupDto>>.Fail("查询采购候选来源失败。");
        }
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<List<PurchaseDraftCandidateDto>>> GetCandidatesAsync(PurchaseDraftCandidateQuery request, CancellationToken cancellationToken = default)
    {
        request ??= new PurchaseDraftCandidateQuery();
        request.SourceType = request.SourceType?.Trim();
        if (request.SourceUid.HasValue != !string.IsNullOrWhiteSpace(request.SourceType) || request.SourceUid == Guid.Empty)
            return ServiceResult<List<PurchaseDraftCandidateDto>>.Fail("INVALID_SOURCE", "来源类型和来源 Uid 必须同时提供或同时为空。");

        try
        {
            var rows = await materialDemandFacts.GetPurchaseCandidatesAsync(request, cancellationToken);
            return ServiceResult<List<PurchaseDraftCandidateDto>>.Ok([.. rows]);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "查询待采购物料失败。");
            return ServiceResult<List<PurchaseDraftCandidateDto>>.Fail("查询待采购物料失败。");
        }
    }

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseDraftPreviewResponse>> PreviewAsync(PurchaseDraftPreviewRequest request, CancellationToken cancellationToken = default)
        => creation.PreviewAsync(request, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(CreatePurchaseDraftsCommand command, CancellationToken cancellationToken = default)
        => creation.CreateAsync(command, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> CancelAsync(Guid uid, CancelPurchaseDraftCommand command, CancellationToken cancellationToken = default)
        => lifecycle.CancelAsync(uid, command, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseDraftReleasePreviewResponse>> PreviewReleaseAsync(Guid uid, PurchaseDraftReleasePreviewRequest request, CancellationToken cancellationToken = default)
        => lifecycle.PreviewReleaseAsync(uid, request, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PurchaseDraftLifecycleResult>> ReleaseAsync(Guid uid, ReleasePurchaseDraftCommand command, CancellationToken cancellationToken = default)
        => lifecycle.ReleaseAsync(uid, command, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<SourcePurchaseTraceDto>> GetSourceTraceAsync(string sourceType, Guid sourceUid, CancellationToken cancellationToken = default)
        => creation.GetSourceTraceAsync(sourceType, sourceUid, cancellationToken);
}

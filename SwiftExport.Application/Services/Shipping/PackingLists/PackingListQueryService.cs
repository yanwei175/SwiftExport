using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Shipping.PackingLists;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>提供装箱单管理、详情及来源候选的只读查询，不占用交付或库存额度。</summary>
public sealed class PackingListQueryService(ServiceBaseContext context, IPackingListQueryRepository repository,
    ICurrentUser currentUser, IPermissionEvaluationService permissions) : IPackingListQueryService
{
    private readonly ILogger<PackingListQueryService> _logger = context.LoggerFactory.CreateLogger<PackingListQueryService>();
    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>> SearchAsync(
        PackingListManagerSearchRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            await RequireSchemaAsync(cancellationToken);
            ValidatePage(request);
            // 非 null 集合完整覆盖对应旧单值，空集合不限；只校验本次生效的筛选维度。
            if (request.CustomerIds is not null && request.CustomerIds.Any(value => value == Guid.Empty))
                Fail(PackingListErrorCodes.InvalidDraft, "客户集合包含空标识。", nameof(request.CustomerIds));
            if (request.CustomerIds is null && request.CustomerId == Guid.Empty)
                Fail(PackingListErrorCodes.InvalidDraft, "客户标识无效。", nameof(request.CustomerId));
            if (request.PostingStatuses is not null && request.PostingStatuses.Any(value => !Enum.IsDefined(value)))
                Fail(PackingListErrorCodes.InvalidDraft, "过账状态集合包含无效值。", nameof(request.PostingStatuses));
            if (request.PostingStatuses is null && request.PostingStatus.HasValue && !Enum.IsDefined(request.PostingStatus.Value))
                Fail(PackingListErrorCodes.InvalidDraft, "过账状态无效。", nameof(request.PostingStatus));
            if (request.InvoiceDateFrom.HasValue && request.InvoiceDateTo.HasValue
                && request.InvoiceDateFrom.Value.Date > request.InvoiceDateTo.Value.Date)
                Fail(PackingListErrorCodes.InvalidDraft, "单据日期起始不能晚于截止日期。", nameof(request.InvoiceDateFrom));
            return await repository.SearchAsync(request, cancellationToken);
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            await RequireSchemaAsync(cancellationToken);
            if (uid == Guid.Empty) Fail(PackingListErrorCodes.InvalidDraft, "装箱单标识不能为空。", nameof(uid));
            return await repository.GetDetailAsync(uid, cancellationToken)
                ?? throw new PackingListException(PackingListErrorCodes.NotFound, "装箱单不存在或已删除。");
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>> SearchSourcesAsync(
        PackingListSourceSearchRequest request, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            await RequireSchemaAsync(cancellationToken);
            ValidatePage(request);
            if (request.CustomerId == Guid.Empty)
                Fail(PackingListErrorCodes.InvalidDraft, "必须指定最终客户。", nameof(request.CustomerId));
            var keywords = (request.Keywords ?? []).Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (keywords.Count > 200 || keywords.Any(x => x.Length > 200) || request.Keyword?.Trim().Length > 200)
                Fail(PackingListErrorCodes.InvalidDraft, "批量搜索最多 200 个关键词，每个最多 200 字符。", nameof(request.Keywords));
            request.Keywords = keywords;
            request.InvoiceNumbers = (request.InvoiceNumbers ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (request.ProductId == Guid.Empty || request.InvoiceNumbers.Count > 200 || request.InvoiceNumbers.Any(x => x.Length > 200))
                Fail(PackingListErrorCodes.InvalidDraft, "来源型号或单号条件无效。");
            var delivery = await repository.SearchSourcesAsync(request, cancellationToken);
            var enrichments = (await repository.GetSourceEnrichmentsAsync(
                delivery.Items.Select(item => item.GrossDemandId).ToArray(),
                cancellationToken)).ToDictionary(item => item.GrossDemandId);
            return new PagedResponseBase<PackingListSourceCandidateDto>
            {
                TotalCount = delivery.TotalCount,
                PageIndex = delivery.PageIndex,
                PageSize = delivery.PageSize,
                Items = delivery.Items.Select(item => ToCandidate(
                    item,
                    enrichments.GetValueOrDefault(item.GrossDemandId))).ToList()
            };
        }, cancellationToken);

    private async Task<T> RunAuthorizedAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        var userId = currentUser.UserId;
        if (!currentUser.IsAuthenticated || !userId.HasValue
            || !await permissions.HasPermissionAsync(userId.Value, "Shipping.PackingList.View", token))
            throw new PackingListException(ServiceErrorCodes.Forbidden, "当前账号没有出货模块访问权限。");
        return await action();
    }

    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return ServiceResult<T>.Ok(await RunAuthorizedAsync(action, token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (PackingListException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DBConcurrencyException ex) { return ServiceResult<T>.Fail(PackingListErrorCodes.VersionConflict, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "装箱单操作失败，未提交变更。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, "装箱单操作失败，未提交变更。");
        }
    }

    private async Task RequireSchemaAsync(CancellationToken token)
    {
        if (!await repository.IsSchemaReadyAsync(token))
            Fail(PackingListErrorCodes.DeliveryUnavailable, "PackingList 新表或幂等索引尚未完整部署。");
    }

    private static void ValidatePage(PagedRequestBase? request)
    {
        if (request is null) Fail(PackingListErrorCodes.InvalidDraft, "查询条件不能为空。");
        request!.PageIndex = Math.Max(1, request.PageIndex);
        request.PageSize = Math.Clamp(request.PageSize, 1, 200);
    }

    /// <summary>把统一交付投影与 PackingList 专属包装补充合并为冻结的 Picker 契约。</summary>
    private static PackingListSourceCandidateDto ToCandidate(
        SourceDemandDeliveryItemDto source,
        PackingListSourceEnrichment? enrichment)
    {
        if (!source.CustomerId.HasValue)
            Fail(PackingListErrorCodes.SourceChanged, "成品需求缺少最终客户，不能用于装箱单。",
                $"Sources[{source.GrossDemandId}].CustomerId");
        return new PackingListSourceCandidateDto
        {
            GrossDemandId = source.GrossDemandId,
            DemandVersion = source.DemandVersion,
            SourceVersion = source.SourceVersion,
            CustomerId = source.CustomerId!.Value,
            ProductId = source.ProductId,
            IsNonStandard = source.IsNonStandard,
            SourceType = source.SourceType,
            SourceNo = source.SourceNo,
            SourceLineNumber = source.SourceLineNumber,
            DemandQty = source.DemandQty,
            DeliveredQty = source.DeliveredQty,
            RemainingQty = source.RemainingQty,
            SkuSnapshot = source.Sku,
            LinkSkuSnapshot = source.LinkSku,
            CustomerSkuSnapshot = source.CustomerSku,
            DescriptionEnSnapshot = source.Description,
            ColorNameEnSnapshot = source.Color,
            UnitSnapshot = source.Unit,
            PcsPerPackage = source.IsNonStandard ? null : enrichment?.PcsPerPackage,
            NwPerPackage = source.IsNonStandard ? null : enrichment?.NwPerPackage,
            GwPerPackage = source.IsNonStandard ? null : enrichment?.GwPerPackage,
            CbmPerPackage = source.IsNonStandard ? null : enrichment?.CbmPerPackage,
            CustomerPoNo = enrichment?.CustomerPoNo,
            InvoiceNo = enrichment?.InvoiceNo,
            ImagePath = source.ImagePath
        };
    }

    private static void Fail(string code, string message, string? field = null) =>
        throw new PackingListException(code, message, field);
}

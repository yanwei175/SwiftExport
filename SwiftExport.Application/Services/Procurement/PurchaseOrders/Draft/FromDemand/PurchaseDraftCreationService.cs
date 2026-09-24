using SwiftExport.Core.Interfaces.DemandPlanning.Sources.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Document;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand;

/// <summary>需求转采购创建编排；每来源独立事务、来源内多供应商原子写入并允许跨来源部分成功。</summary>
public sealed class PurchaseDraftCreationService(
    ServiceBaseContext context,
    IGrossDemandDocumentRepository grossDemandRepository,
    IMaterialDemandDocumentRepository netDemandRepository,
    INetDemandExecutionLinkRepository executionLinkRepository,
    IPurchaseOrderDraftRepository purchaseDraftRepository,
    IDocumentNoService documentNoService,
    PurchaseDraftPreviewService previewPreparation,
    PurchaseDraftPreviewBuilder previewBuilder,
    PurchaseDraftWriteModelBuilder writeModelBuilder) : IPurchaseDraftCreationService
{
    private readonly DbSession _session = context.Session;
    private readonly ILogger<PurchaseDraftCreationService> _logger =
        context.LoggerFactory.CreateLogger<PurchaseDraftCreationService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(
        CreatePurchaseDraftsCommand command,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ValidateCreateCommand(command);
        }
        catch (PurchaseDraftException ex)
        {
            return ServiceResult<CreatePurchaseDraftsResult>.Fail(ex.Code, ex.Message);
        }

        var result = new CreatePurchaseDraftsResult { ExecutionRequestId = command.ExecutionRequestId };
        foreach (var source in command.Sources
                     .OrderBy(x => x.SourceType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.SourceUid))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Sources.Add(await CreateSourceAsync(command, source, cancellationToken));
        }
        return ServiceResult<CreatePurchaseDraftsResult>.Ok(result);
    }

    /// <summary>每来源独立开启事务，依固定锁序复核重放或写入全部供应商草稿；提交后才发布结果。</summary>
    private async Task<CreatePurchaseDraftSourceResult> CreateSourceAsync(
        CreatePurchaseDraftsCommand command,
        CreatePurchaseDraftSourceCommand source,
        CancellationToken cancellationToken)
    {
        var result = new CreatePurchaseDraftSourceResult
        {
            SourceType = source.SourceType,
            SourceUid = source.SourceUid,
            Outcome = NetDemandCommandOutcomeEnum.Failed
        };
        var resultBuffer = new PurchaseDraftSourceResultBuffer(result);
        if (_session.Transaction is not null)
        {
            result.ErrorCode = "TRANSACTION_ALREADY_ACTIVE";
            result.Message = "当前请求存在未完成事务。";
            return result;
        }

        _session.Transaction = _session.Connection.BeginTransaction();
        try
        {
            // 固定锁序：GrossDemand -> NetDemand -> Link/CommandItem -> PurchaseOrder。
            var grossDemands = await grossDemandRepository.GetActiveBySourceForUpdateAsync(source.SourceType, source.SourceUid);
            if (grossDemands.Count == 0)
                throw new PurchaseDraftException("SOURCE_NOT_FOUND", "来源单没有活动毛需求。");

            var netDemands = new List<NetDemand>();
            foreach (var grossDemand in grossDemands)
                netDemands.AddRange(await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(grossDemand.Uid));
            if (netDemands.Count == 0)
                throw new PurchaseDraftException("CORRUPTED_STATE", "来源单没有活动净需求。");

            var activeLinks = await executionLinkRepository.GetActiveByNetDemandIdsForUpdateAsync(
                netDemands.Select(x => x.Uid));
            var existingCommandLinks = await executionLinkRepository.GetByCommandItemIdsForUpdateAsync(
                source.Items.Select(x => x.CommandItemId));

            if (existingCommandLinks.Count > 0)
            {
                var replay = await TryBuildReplayAsync(command, source, existingCommandLinks, cancellationToken);
                if (replay is null)
                    throw new PurchaseDraftException("IDEMPOTENCY_PARTIAL_STATE",
                        "执行请求只有部分命令项已写入，数据需要人工检查。");
                _session.Transaction.Commit();
                return replay;
            }

            var buildRequest = await previewPreparation.BuildLockedPreviewRequestAsync(
                command, source, grossDemands, netDemands, activeLinks, cancellationToken);
            var preview = previewBuilder.Build(buildRequest);
            var sourcePreview = preview.SourceGroups.SingleOrDefault(x =>
                x.SourceUid == source.SourceUid
                && string.Equals(x.SourceType, source.SourceType, StringComparison.OrdinalIgnoreCase));
            if (sourcePreview is null || sourcePreview.Documents.Count == 0)
                throw new PurchaseDraftException("NOTHING_TO_CREATE", "来源单没有可转采购的剩余数量。");
            if (sourcePreview.HasBlockingWarning())
                throw new PurchaseDraftException("PREVIEW_BLOCKED",
                    string.Join("；", sourcePreview.Warnings.Where(x => x.IsBlocker).Select(x => x.Message)));
            if (!string.Equals(sourcePreview.SourcePreviewToken, source.SourcePreviewToken, StringComparison.Ordinal))
                throw new PurchaseDraftException("PREVIEW_EXPIRED", "来源事实已变化，请重新预览。");

            var confirmedCodes = source.ConfirmedWarningCodes
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToHashSet(StringComparer.Ordinal);
            var unconfirmed = sourcePreview.Warnings
                .Where(x => x.RequiresConfirmation && !confirmedCodes.Contains(x.Code))
                .Select(x => x.Code)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            if (unconfirmed.Length > 0)
                throw new PurchaseDraftException("WARNING_CONFIRMATION_REQUIRED",
                    $"以下强警告尚未确认：{string.Join(",", unconfirmed)}。");

            var targetBySupplier = ValidateAndIndexTargets(command.EntryMode, source, sourcePreview.Documents);
            foreach (var document in sourcePreview.Documents.OrderBy(x => x.SupplierId))
            {
                var target = targetBySupplier.GetValueOrDefault(document.SupplierId);
                var targetMode = target?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew;
                PurchaseOrderHead? appendHead = null;
                Guid purchaseOrderId;
                string orderNo;
                int firstLineNumber;

                if (targetMode == PurchaseDraftTargetModeEnum.AppendExisting)
                {
                    appendHead = await purchaseDraftRepository.GetSafeDraftForUpdateAsync(
                        target!.ExistingPurchaseOrderId!.Value,
                        target.ExpectedPurchaseOrderVersion!.Value)
                        ?? throw new PurchaseDraftException("APPEND_TARGET_UNSAFE",
                            "目标采购草稿已变化、已确认或已有收货/库存事实。");
                    ValidateAppendHead(appendHead, source, document);
                    purchaseOrderId = appendHead.Uid;
                    orderNo = appendHead.OrderNo;
                    firstLineNumber = await purchaseDraftRepository.GetMaxLineNumberForUpdateAsync(purchaseOrderId) + 1;
                }
                else
                {
                    var duplicateHead = await purchaseDraftRepository.GetByExecutionScopeForUpdateAsync(
                        command.ExecutionRequestId, source.SourceType, source.SourceUid, document.SupplierId);
                    if (duplicateHead is not null)
                        throw new PurchaseDraftException("IDEMPOTENCY_PARTIAL_STATE",
                            "已存在同执行请求采购头但缺少完整 Link，数据需要人工检查。");

                    var number = await documentNoService.GetNextDocumentNoAsync(new DocumentNoRequest
                    {
                        DocType = DocumentTypeStaticsEnum.PurchaseOrder,
                        BusinessDate = DateTime.Now
                    });
                    if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo))
                        throw new PurchaseDraftException("DOCUMENT_NO_FAILED", number.Message ?? "采购单号生成失败。");
                    purchaseOrderId = GuidSequentialGenerator.NewGuid();
                    orderNo = number.Data.DocumentNo;
                    firstLineNumber = 1;
                }

                var writeModel = writeModelBuilder.Build(
                    document, command.ExecutionRequestId, purchaseOrderId, orderNo,
                    firstLineNumber, targetMode == PurchaseDraftTargetModeEnum.CreateNew);
                if (writeModel.Head is not null
                    && await purchaseDraftRepository.InsertPurchaseDraftAsync(writeModel.Head) != 1)
                    throw new PurchaseDraftException("PURCHASE_HEAD_INSERT_FAILED", "采购草稿头写入失败。");
                if (await purchaseDraftRepository.InsertPurchaseDraftLinesAsync(writeModel.Lines) != writeModel.Lines.Count)
                    throw new PurchaseDraftException("PURCHASE_LINE_INSERT_FAILED", "采购草稿行写入不完整。");
                if (await executionLinkRepository.InsertPurchaseLinksAsync(writeModel.Links) != writeModel.Links.Count)
                    throw new PurchaseDraftException("PURCHASE_LINK_INSERT_FAILED", "采购追溯 Link 写入不完整。");
                if (appendHead is not null
                    && await purchaseDraftRepository.AdvanceDraftVersionAfterAppendAsync(
                        appendHead.Uid, appendHead.Version) != 1)
                    throw new PurchaseDraftException("APPEND_CONCURRENCY_CONFLICT", "追加采购草稿时版本已变化。");

                resultBuffer.Stage(new CreatedPurchaseDraftDto
                {
                    PurchaseOrderId = purchaseOrderId,
                    Version = appendHead is null ? 1 : appendHead.Version + 1,
                    OrderNo = orderNo,
                    SupplierId = document.SupplierId,
                    SupplierCode = document.SupplierCode,
                    SupplierName = document.SupplierName,
                    AppendedToExisting = appendHead is not null
                });
            }

            _session.Transaction.Commit();
            resultBuffer.PublishCommitted();
            result.Outcome = NetDemandCommandOutcomeEnum.Succeeded;
            result.Message = "采购草稿创建成功。";
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _session.Transaction.Rollback();
            resultBuffer.Rollback();
            throw;
        }
        catch (PurchaseDraftException ex)
        {
            _session.Transaction.Rollback();
            resultBuffer.Rollback();
            result.ErrorCode = ex.Code;
            result.Message = ex.Message;
            return result;
        }
        catch (Exception ex)
        {
            _session.Transaction.Rollback();
            resultBuffer.Rollback();
            _logger.LogError(ex, "来源 {SourceType}/{SourceUid} 创建采购草稿失败。", source.SourceType, source.SourceUid);
            result.ErrorCode = "UNEXPECTED_ERROR";
            result.Message = "创建采购草稿失败。";
            return result;
        }
        finally
        {
            _session.Transaction.Dispose();
            _session.Transaction = null;
        }
    }

    /// <summary>按 ExecutionRequestId 与全部 CommandItemId 核验已有 Link，只允许完整来源重放。</summary>
    private async Task<CreatePurchaseDraftSourceResult?> TryBuildReplayAsync(
        CreatePurchaseDraftsCommand command,
        CreatePurchaseDraftSourceCommand source,
        IReadOnlyList<DemandExecutionLink> links,
        CancellationToken cancellationToken)
    {
        if (links.Count != source.Items.Count) return null;
        var byCommandItem = links
            .Where(x => x.CommandItemId != Guid.Empty)
            .GroupBy(x => x.CommandItemId)
            .ToDictionary(x => x.Key, x => x.ToList());
        if (byCommandItem.Count != source.Items.Count) return null;
        foreach (var item in source.Items)
        {
            if (!byCommandItem.TryGetValue(item.CommandItemId, out var matches) || matches.Count != 1)
                return null;
            var link = matches[0];
            if (link.ExecutionRequestId != command.ExecutionRequestId
                || link.NetDemandId != item.NetDemandId
                || link.AllocatedQty != item.PurchaseQty)
                throw new PurchaseDraftException("IDEMPOTENCY_KEY_REUSED",
                    "CommandItemId 已被不同的执行事实使用。");
        }

        var heads = await purchaseDraftRepository.GetPurchaseDraftsByIdsAsync(
            links.Select(x => x.TargetHeadId), cancellationToken);
        if (heads.Count != links.Select(x => x.TargetHeadId).Distinct().Count()) return null;
        return new CreatePurchaseDraftSourceResult
        {
            SourceType = source.SourceType,
            SourceUid = source.SourceUid,
            Outcome = NetDemandCommandOutcomeEnum.AlreadyApplied,
            Message = "该来源已创建采购草稿。",
            PurchaseDrafts = heads.OrderBy(x => x.OrderNo).Select(x => new CreatedPurchaseDraftDto
            {
                PurchaseOrderId = x.Uid,
                Version = x.Version,
                OrderNo = x.OrderNo,
                SupplierId = x.SupplierId,
                SupplierCode = x.SupplierCode,
                SupplierName = x.SupplierName,
                AppendedToExisting = x.ExecutionRequestId != command.ExecutionRequestId
            }).ToList()
        };
    }

    /// <summary>核对每供应商目标唯一且符合入口和追加版本要求。</summary>
    private static Dictionary<Guid, CreatePurchaseDraftTargetCommand> ValidateAndIndexTargets(
        PurchaseDraftEntryModeEnum entryMode,
        CreatePurchaseDraftSourceCommand source,
        IReadOnlyList<PurchaseDraftPreviewDocumentDto> documents)
    {
        if (source.Targets.GroupBy(x => x.SupplierId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException("DUPLICATE_TARGET", "同一供应商存在重复采购草稿目标。");
        var documentSuppliers = documents.Select(x => x.SupplierId).ToHashSet();
        if (source.Targets.Any(x => !documentSuppliers.Contains(x.SupplierId)))
            throw new PurchaseDraftException("INVALID_TARGET", "采购草稿目标包含本来源不存在的供应商。");
        foreach (var target in source.Targets)
        {
            if (entryMode == PurchaseDraftEntryModeEnum.SourceOneClick
                && target.TargetMode != PurchaseDraftTargetModeEnum.CreateNew)
                throw new PurchaseDraftException("ONE_CLICK_MUST_CREATE_NEW", "来源一键转采购只能新建草稿。");
            if (target.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting
                && (!target.ExistingPurchaseOrderId.HasValue
                    || target.ExistingPurchaseOrderId.Value == Guid.Empty
                    || !target.ExpectedPurchaseOrderVersion.HasValue
                    || target.ExpectedPurchaseOrderVersion.Value <= 0))
                throw new PurchaseDraftException("APPEND_TARGET_REQUIRED", "追加采购草稿必须携带目标 Uid 和版本。");
        }
        return source.Targets.ToDictionary(x => x.SupplierId);
    }

    /// <summary>验证追加目标的客户身份、来源、供应商和安全状态一致；名称与代码快照仍沿用原头。</summary>
    private static void ValidateAppendHead(
        PurchaseOrderHead head,
        CreatePurchaseDraftSourceCommand source,
        PurchaseDraftPreviewDocumentDto document)
    {
        if (head.CustomerId != document.CustomerId
            || head.SupplierId != document.SupplierId
            || head.DemandSourceUid != source.SourceUid
            || !string.Equals(head.DemandSourceType, source.SourceType, StringComparison.OrdinalIgnoreCase)
            || head.LifecycleStatus != PurchaseOrderLifecycleStatusEnum.Active
            || head.ConfirmationStatus != ConfirmationStatusEnum.待确认)
            throw new PurchaseDraftException("APPEND_TARGET_MISMATCH", "目标采购草稿与客户、来源或供应商不一致。");
    }

    /// <summary>在任何来源事务前验证执行身份、来源集合及命令项唯一性。</summary>
    private static void ValidateCreateCommand(CreatePurchaseDraftsCommand command)
    {
        if (command is null || command.ExecutionRequestId == Guid.Empty
            || string.IsNullOrWhiteSpace(command.PreviewToken))
            throw new PurchaseDraftException("INVALID_COMMAND", "执行请求和预览 Token 不能为空。");
        if (!Enum.IsDefined(command.EntryMode))
            throw new PurchaseDraftException("INVALID_ENTRY_MODE", "采购入口模式无效。");
        if (command.Sources is null || command.Sources.Count == 0)
            throw new PurchaseDraftException("SOURCE_REQUIRED", "至少提交一个来源单。");
        if (command.Sources.Any(x => x is null))
            throw new PurchaseDraftException("INVALID_SOURCE", "来源命令不能为空。");
        if (command.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick && command.Sources.Count != 1)
            throw new PurchaseDraftException("ONE_CLICK_SINGLE_SOURCE", "来源一键转采购只能提交一个来源单。");
        if (command.Sources.Count == 1
            && !string.Equals(command.PreviewToken, command.Sources[0].SourcePreviewToken, StringComparison.Ordinal))
            throw new PurchaseDraftException("PREVIEW_TOKEN_MISMATCH", "单来源预览 Token 与来源 Token 不一致。");
        if (command.Sources.GroupBy(x => (x.SourceType?.Trim().ToUpperInvariant(), x.SourceUid)).Any(x => x.Count() > 1))
            throw new PurchaseDraftException("DUPLICATE_SOURCE", "执行命令包含重复来源单。");
        var allItems = command.Sources.SelectMany(x => x.Items ?? new List<CreatePurchaseDraftItemCommand>()).ToList();
        if (allItems.GroupBy(x => x.CommandItemId).Any(x => x.Count() > 1)
            || allItems.GroupBy(x => x.NetDemandId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException("DUPLICATE_ITEM", "不同来源之间不能重复提交命令项或物料需求。");
        foreach (var source in command.Sources)
        {
            source.SourceType = source.SourceType?.Trim();
            source.Targets ??= new List<CreatePurchaseDraftTargetCommand>();
            source.ConfirmedWarningCodes ??= new List<string>();
            if (string.IsNullOrWhiteSpace(source.SourceType) || source.SourceUid == Guid.Empty
                || string.IsNullOrWhiteSpace(source.SourcePreviewToken)
                || source.Items is null || source.Items.Count == 0
                || !Enum.IsDefined(source.LineMode))
                throw new PurchaseDraftException("INVALID_SOURCE", "来源命令缺少来源、Token、模式或计划项。");
            ValidateCreateItems(source.Items);
        }
    }

    /// <summary>拒绝缺失身份、无效版本数量与来源内重复命令项。</summary>
    private static void ValidateCreateItems(IReadOnlyList<CreatePurchaseDraftItemCommand> items)
    {
        if (items.Any(x => x.CommandItemId == Guid.Empty || x.NetDemandId == Guid.Empty || x.SupplierId == Guid.Empty
                           || x.ExpectedVersion <= 0 || x.ExpectedRemainingQty <= 0 || x.PurchaseQty <= 0)
            || items.GroupBy(x => x.CommandItemId).Any(x => x.Count() > 1)
            || items.GroupBy(x => x.NetDemandId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException("INVALID_ITEM", "执行命令包含无效或重复的物料需求项。");
    }

}

/// <summary>统一判断来源预览是否含阻断项或未满足创建条件。</summary>
internal static class PurchaseDraftPreviewSourceGroupExtensions
{
    /// <summary>阻断警告或不可创建状态均阻止来源提交。</summary>
    public static bool HasBlockingWarning(this PurchaseDraftPreviewSourceGroupDto source)
        => source.Warnings.Any(x => x.IsBlocker) || !source.CanCreate;
}

/// <summary>确保同一来源事务只有在提交成功后才向 API 结果发布采购草稿。</summary>
public sealed class PurchaseDraftSourceResultBuffer(CreatePurchaseDraftSourceResult result)
{
    private readonly List<CreatedPurchaseDraftDto> _pending = [];

    /// <summary>仅暂存尚未提交的采购草稿结果。</summary>
    public void Stage(CreatedPurchaseDraftDto draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        _pending.Add(draft);
    }

    /// <summary>事务提交成功后一次性发布本来源结果。</summary>
    public void PublishCommitted()
    {
        result.PurchaseDrafts.AddRange(_pending);
        _pending.Clear();
    }

    /// <summary>事务回滚时丢弃待发布草稿，不向调用方泄露未提交身份。</summary>
    public void Rollback()
    {
        _pending.Clear();
        result.PurchaseDrafts.Clear();
    }
}

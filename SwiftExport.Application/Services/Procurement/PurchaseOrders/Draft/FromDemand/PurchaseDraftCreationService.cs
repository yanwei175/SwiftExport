using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Application.Services.Systems.Numbering;
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
using SwiftExport.Core.Entities.SalesOrders;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Entities.Relations;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.DemandPlanning;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Application.Commands;
using System.Security.Cryptography;
using System.Text;

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
    PurchaseDraftWriteModelBuilder writeModelBuilder,
    BusinessCommandExecutor executor,
    ProcurementOrderCommandSupport support) : IPurchaseDraftCreationService
{
    private readonly IDbSession _session = context.Session;
    private readonly ILogger<PurchaseDraftCreationService> _logger =
        context.LoggerFactory.CreateLogger<PurchaseDraftCreationService>();

    /// <inheritdoc/>
    public async Task<ServiceResult<CreatePurchaseDraftsResult>> CreateAsync(
        CreatePurchaseDraftsCommand command,
        CancellationToken cancellationToken = default)
    {
        if (await support.AuthorizeAsync<CreatePurchaseDraftsResult>("Create", cancellationToken) is { } denied) return denied;
        try
        {
            ValidateCreateCommand(command);
        }
        catch (PurchaseDraftException ex)
        {
            return ServiceResult<CreatePurchaseDraftsResult>.Fail(ex.Code, ex.Message);
        }
        if(command.Sources.SelectMany(source=>source.Targets).Any(target=>target.TargetMode==PurchaseDraftTargetModeEnum.AppendExisting)
            && await support.AuthorizeAsync<CreatePurchaseDraftsResult>("Edit",cancellationToken) is {} appendDenied) return appendDenied;

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

    /// <summary>每来源先准备PG需要的命令项及来源资源，再独立开启原事务复核重放或写入供应商草稿；事务结束才归还资源，提交后发布结果。</summary>
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
        if (_session.Transaction is not null)
        {
            result.ErrorCode = PurchaseDraftErrorCodes.TransactionAlreadyActive;
            result.Message = "当前请求存在未完成事务。";
            return result;
        }

        var commandId = SourceCommandId(command.ExecutionRequestId, source.SourceType, source.SourceUid);
        var rollbackConfirmed = false;
        try
        {
            // 命令准备先于来源准备和首次快照；回执在来源业务校验前重放，删除草稿后也不会复建。
            var preparation = netDemandRepository as IMaterialDemandTransactionPreparation;
            var executed = await executor.ExecuteAsync(commandId, "Procurement.Demand.CreateDraft", source.SourceUid,
                new { command.EntryMode, SourceType = source.SourceType.ToUpperInvariant(), source.SourceUid, source.LineMode,
                    Items = source.Items.OrderBy(x => x.CommandItemId).ToArray(),
                    Targets = source.Targets.OrderBy(x => x.SupplierId).ToArray() },
                async token =>
                {
                    var created = await CreateLockedSourceAsync(command, source, token);
                    await support.RecordDemandDraftsAsync(commandId,command.ExecutionRequestId,created,token);
                    return created;
                }, cancellationToken, beforeTransaction: preparation is null ? null : token =>
                    preparation.AcquireMaterialBeforeTransactionAsync([source.SourceUid], [],
                        source.Items.Select(x => x.NetDemandId).Order().ToArray(),
                        source.Items.Select(x => x.CommandItemId).Order().ToArray(), token),
                onConfirmedRollback:()=>rollbackConfirmed=true);
            if (executed.IsReplay) executed.Value.Outcome = NetDemandCommandOutcomeEnum.AlreadyApplied;
            return executed.Value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (PurchaseDraftException ex) { result.ErrorCode=ex.Code;result.Message=ex.Message;return result; }
        catch (BusinessCommandException ex)
        {
            _logger.LogWarning(ex,"需求采购命令返回 {ErrorCode}；ExecutionRequestId={ExecutionRequestId}，SourceCommandId={SourceCommandId}。",ex.Code,command.ExecutionRequestId,commandId);
            result.ErrorCode=ex.Code;result.Message=ex.Message;return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,"来源 {SourceType}/{SourceUid} 创建采购草稿失败；ExecutionRequestId={ExecutionRequestId}，SourceCommandId={SourceCommandId}，RollbackConfirmed={RollbackConfirmed}。",
                source.SourceType,source.SourceUid,command.ExecutionRequestId,commandId,rollbackConfirmed);
            result.ErrorCode=rollbackConfirmed?ServiceErrorCodes.WriteFailed:ServiceErrorCodes.OutcomeUnknown;
            result.Message=rollbackConfirmed?"该来源采购创建失败，事务已回滚，未提交采购结果。可保留原请求重试或检查服务日志。":"采购创建结果待核实，请保留原执行请求和输入重试。";
            return result;
        }
    }

    /// <summary>在公共执行器拥有的每来源事务内锁定需求和全部占用，重新验证容量后原子创建或追加。</summary>
    private async Task<CreatePurchaseDraftSourceResult> CreateLockedSourceAsync(CreatePurchaseDraftsCommand command,
        CreatePurchaseDraftSourceCommand source,CancellationToken cancellationToken)
    {
            var result = new CreatePurchaseDraftSourceResult {SourceType=source.SourceType,SourceUid=source.SourceUid};
            // 固定锁序：GrossDemand -> NetDemand -> Link/CommandItem -> PurchaseOrder。
            var grossDemands = await grossDemandRepository.GetActiveBySourceForUpdateAsync(source.SourceType, source.SourceUid);
            if (grossDemands.Count == 0)
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.SourceNotFound, "来源单没有活动成品需求。");

            var netDemands = new List<NetDemand>();
            foreach (var grossDemand in grossDemands)
                netDemands.AddRange(await netDemandRepository.GetActiveByGrossDemandForUpdateAsync(grossDemand.Uid));
            if (netDemands.Count == 0)
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.CorruptedState, "来源单没有活动净需求。");

            var activeLinks = await executionLinkRepository.GetActiveByNetDemandIdsForUpdateAsync(
                netDemands.Select(x => x.Uid));
            var existingCommandLinks = await executionLinkRepository.GetByCommandItemIdsForUpdateAsync(
                source.Items.Select(x => x.CommandItemId));

            if (existingCommandLinks.Count > 0)
            {
                // 旧Link不能证明完整商务输入；无公共回执时保留事实并拒绝重建，不能猜测重放结果。
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.IdempotencyPartialState,
                    "命令项已有执行事实但缺少完整命令回执，请核实原执行结果，不得重新创建。");
            }

            var buildRequest = await previewPreparation.BuildLockedPreviewRequestAsync(
                command, source, grossDemands, netDemands, activeLinks, cancellationToken);
            var preview = previewBuilder.Build(buildRequest);
            var sourcePreview = preview.SourceGroups.SingleOrDefault(x =>
                x.SourceUid == source.SourceUid
                && string.Equals(x.SourceType, source.SourceType, StringComparison.OrdinalIgnoreCase));
            if (sourcePreview is null)
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.NothingToCreate, "来源单没有可转采购的剩余数量。");
            if (sourcePreview.HasBlockingWarning())
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.PreviewBlocked,
                    string.Join("；", sourcePreview.Warnings.Where(x => x.IsBlocker).Select(x => x.Message)));
            if(sourcePreview.Documents.Count==0)
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.NothingToCreate,"来源单没有可创建采购草稿的有效物料。");

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
                        ?? throw new PurchaseDraftException(PurchaseDraftErrorCodes.AppendTargetUnsafe,
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
                        throw new PurchaseDraftException(PurchaseDraftErrorCodes.IdempotencyPartialState,
                            "已存在同执行请求采购头但缺少完整 Link，数据需要人工检查。");

                    // 追加既有草稿不占新号；首次创建仍需复核全库 BONO，不能依赖预览。
                    if (await purchaseDraftRepository.BoNoExistsAsync(document.Header.BoNo, cancellationToken))
                        throw new PurchaseDraftException(SwiftExport.Contracts.DTOs.Errors.ServiceErrorCodes.Validation,
                            "自定义采购单号（BONO）已被未删除的采购订单使用，请修改后重新保存。");

                    var number = await documentNoService.GetNextDocumentNoAsync(new DocumentNoRequest
                    {
                        DocType = DocumentTypeStaticsEnum.PurchaseOrder,
                        BusinessDate = DateTime.Now
                    });
                    if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo))
                        throw new PurchaseDraftException(PurchaseDraftErrorCodes.DocumentNoFailed, number.Message ?? "采购单号生成失败。");
                    purchaseOrderId = GuidSequentialGenerator.NewGuid();
                    orderNo = number.Data.DocumentNo;
                    firstLineNumber = 1;
                }

                var writeModel = writeModelBuilder.Build(
                    document, command.ExecutionRequestId, purchaseOrderId, orderNo,
                    firstLineNumber, targetMode == PurchaseDraftTargetModeEnum.CreateNew);
                if (writeModel.Head is not null)
                {
                    var write = await purchaseDraftRepository.InsertPurchaseDraftAsync(writeModel.Head);
                    if (!write.Success)
                        throw new PurchaseDraftException(write.ErrorCode ?? PurchaseDraftErrorCodes.PurchaseHeadInsertFailed,
                            write.Message ?? "采购草稿头写入失败。");
                }
                if (await purchaseDraftRepository.InsertPurchaseDraftLinesAsync(writeModel.Lines) != writeModel.Lines.Count)
                    throw new PurchaseDraftException(PurchaseDraftErrorCodes.PurchaseLineInsertFailed, "采购草稿行写入不完整。");
                if (await executionLinkRepository.InsertPurchaseLinksAsync(writeModel.Links) != writeModel.Links.Count)
                    throw new PurchaseDraftException(PurchaseDraftErrorCodes.PurchaseLinkInsertFailed, "采购追溯 Link 写入不完整。");
                if (appendHead is not null
                    && await purchaseDraftRepository.AdvanceDraftVersionAfterAppendAsync(
                        appendHead.Uid, appendHead.Version) != 1)
                    throw new PurchaseDraftException(PurchaseDraftErrorCodes.AppendConcurrencyConflict, "追加采购草稿时版本已变化。");

                result.PurchaseDrafts.Add(new CreatedPurchaseDraftDto
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

            result.Outcome = NetDemandCommandOutcomeEnum.Succeeded;
            result.Message = "采购草稿创建成功。";
            return result;
    }

    /// <summary>核对每供应商目标唯一且符合入口和追加版本要求。</summary>
    private static Dictionary<Guid, CreatePurchaseDraftTargetCommand> ValidateAndIndexTargets(
        PurchaseDraftEntryModeEnum entryMode,
        CreatePurchaseDraftSourceCommand source,
        IReadOnlyList<PurchaseDraftPreviewDocumentDto> documents)
    {
        if (source.Targets.GroupBy(x => x.SupplierId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.DuplicateTarget, "同一供应商存在重复采购草稿目标。");
        var documentSuppliers = documents.Select(x => x.SupplierId).ToHashSet();
        if (source.Targets.Any(x => !documentSuppliers.Contains(x.SupplierId)))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidTarget, "采购草稿目标包含本来源不存在的供应商。");
        foreach (var target in source.Targets)
        {
            if (entryMode == PurchaseDraftEntryModeEnum.SourceOneClick
                && target.TargetMode != PurchaseDraftTargetModeEnum.CreateNew)
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.OneClickMustCreateNew, "来源一键转采购只能新建草稿。");
            if (target.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting
                && (!target.ExistingPurchaseOrderId.HasValue
                    || target.ExistingPurchaseOrderId.Value == Guid.Empty
                    || !target.ExpectedPurchaseOrderVersion.HasValue
                    || target.ExpectedPurchaseOrderVersion.Value <= 0))
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.AppendTargetRequired, "追加采购草稿必须携带目标 Uid 和版本。");
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
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.AppendTargetMismatch, "目标采购草稿与客户、来源或供应商不一致。");
    }

    /// <summary>在任何来源事务前验证执行身份、来源集合及命令项唯一性。</summary>
    private static void ValidateCreateCommand(CreatePurchaseDraftsCommand command)
    {
        if (command is null || command.ExecutionRequestId == Guid.Empty)
            throw new PurchaseDraftException(ServiceErrorCodes.InvalidCommand, "执行请求不能为空。");
        if (!Enum.IsDefined(command.EntryMode))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidEntryMode, "采购入口模式无效。");
        if (command.Sources is null || command.Sources.Count is <1 or >200)
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.SourceRequired, "至少提交一个来源单。");
        if (command.Sources.Any(x => x is null))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidSource, "来源命令不能为空。");
        if (command.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick && command.Sources.Count != 1)
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.OneClickSingleSource, "来源一键转采购只能提交一个来源单。");
        if (command.Sources.GroupBy(x => (x.SourceType?.Trim().ToUpperInvariant(), x.SourceUid)).Any(x => x.Count() > 1))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.DuplicateSource, "执行命令包含重复来源单。");
        var allItems = command.Sources.SelectMany(x => x.Items ?? new List<CreatePurchaseDraftItemCommand>()).ToList();
        if(allItems.Any(item=>item is null))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidItem,"采购物料项不能为空。");
        if (allItems.GroupBy(x => x.CommandItemId).Any(x => x.Count() > 1)
            || allItems.GroupBy(x => x.NetDemandId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.DuplicateItem, "不同来源之间不能重复提交命令项或物料需求。");
        foreach (var source in command.Sources)
        {
            source.SourceType = source.SourceType?.Trim();
            source.Targets ??= new List<CreatePurchaseDraftTargetCommand>();
            if(source.Targets.Any(target=>target is null))
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidTarget,"采购目标不能为空。");
            if (string.IsNullOrWhiteSpace(source.SourceType) || source.SourceUid == Guid.Empty
                || source.Items is null || source.Items.Count == 0
                || !Enum.IsDefined(source.LineMode))
                throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidSource, "来源命令缺少来源、模式或计划项。");
            ValidateCreateItems(source.Items);
        }
    }

    /// <summary>拒绝缺失身份、无效版本数量、超出存储精度的价格金额及来源内重复命令项。</summary>
    private static void ValidateCreateItems(IReadOnlyList<CreatePurchaseDraftItemCommand> items)
    {
        if (items.Count>500 || items.Any(x => x.CommandItemId == Guid.Empty || x.NetDemandId == Guid.Empty || x.SupplierId == Guid.Empty
                           || x.ExpectedVersion <= 0
                           || x.DeliveryTargetSupplierId==Guid.Empty)
            || items.GroupBy(x => x.CommandItemId).Any(x => x.Count() > 1)
            || items.GroupBy(x => x.NetDemandId).Any(x => x.Count() > 1))
            throw new PurchaseDraftException(PurchaseDraftErrorCodes.InvalidItem, "执行命令包含无效或重复的物料需求项。");
        foreach(var item in items)
        {
            try
            {
                ProcurementOrderPolicy.Number(item.PurchaseQty,4,"Items[].PurchaseQty",true);
                _ = ProcurementOrderPolicy.CalculateAmounts(item.PurchaseQty, item.UnitPrice, item.TaxPrice);
            }
            catch(ProcurementOperationException ex)
            {throw new PurchaseDraftException(ex.Code,$"物料 {item.NetDemandId}：{ex.Message}");}
        }
    }

    /// <summary>同一执行请求内按来源类型及身份派生稳定回执键，不依赖可变SKU或预览内容。</summary>
    internal static Guid SourceCommandId(Guid executionRequestId,string sourceType,Guid sourceUid)
        => new(SHA256.HashData(Encoding.UTF8.GetBytes($"Procurement.Demand.CreateDraft|{executionRequestId:D}|{sourceType.Trim().ToUpperInvariant()}|{sourceUid:D}"))[..16]);

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

using System.Data;
using System.Text.Json;
using SwiftExport.Application.Commands;
using System.Security.Cryptography;
using System.Text;
using Mapster;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Shipping.PackingLists;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>箱单整单反向入口与编排；依据原执行证据恢复库存，不重新解释当前需求和 BOM。</summary>
public sealed class PackingListReversalService(ServiceBaseContext context, IPackingListDocumentRepository packingLists,
    IPackingListPostingFactsRepository factsRepository, IPackingListExecutionRepository executions,
    IAutomaticPostingScope scope, IInboundPostingPort inbound, IOutboundPostingPort outbound,
    IBusinessOperationLogRepository logs, ICurrentUser user, IUserPermissionService permissions,
    BusinessCommandExecutor executor, CommandPipeline pipeline, TypeAdapterConfig mapping) : IPackingListReversalService
{
    /// <summary>只读展示原执行单据与库存反向影响，不重新计算当前采购分配。</summary>
    public async Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid,
        PackingListReversePreviewRequest request, CancellationToken token)
    {
        if (request is null) return ServiceResult<PackingListPostingPreviewDto>.Fail("INVALID_COMMAND", "反向预览请求不能为空。");
        try
        {
            var preview = await ReversePreviewCoreAsync(uid, request.ExpectedVersion, request.BusinessDate, request.Reason ?? "", token);
            var detail = await packingLists.GetDetailAsync(uid, token);
            if (detail?.Header == null || detail.Header.Version != request.ExpectedVersion)
                return ServiceResult<PackingListPostingPreviewDto>.Fail("VERSION_CONFLICT", "箱单已变化，请刷新后预览。");
            var result = new PackingListPostingPreviewDto { PackingListId = uid, ExpectedVersion = request.ExpectedVersion,
                BusinessDate = request.BusinessDate.Date, IsReverse = true, PreviewToken = preview.Token,
                Actions = preview.Actions.Select(x => x.Adapt<PackingListPostingActionDto>(mapping)).ToList(),
                Blockers = preview.Blockers.Select(x => x.Adapt<PackingListPostingBlockerDto>(mapping)).ToList() };
            var warehouses = (await executions.ReadReverseFactsAsync(uid, token)).Warehouses;
            foreach (var action in result.Actions)
            {
                var line = detail.Lines.FirstOrDefault(x => x.Uid == action.PackingListLineId);
                action.SourceNo = line?.SourceDocumentNoSnapshot;
                if (line?.ProductId == action.ProductId)
                {
                    action.Sku = line?.SkuSnapshot; action.CustomerSku = line?.CustomerSkuSnapshot; action.ImagePath = line?.ImagePath;
                }
                var warehouse = warehouses.FirstOrDefault(x => x.Uid == action.WarehouseSiteId);
                action.WarehouseCode = warehouse?.Code; action.WarehouseName = warehouse?.Name;
                action.Explanation = action.ActionKind == "Delivery" ? "售后直发或非标来源：仅登记交付，不产生库存流水。" : null;
            }
            return ServiceResult<PackingListPostingPreviewDto>.Ok(result);
        }
        catch (PackingListPostingFailure error) { return ServiceResult<PackingListPostingPreviewDto>.Fail(error.Code, error.Message); }
        catch (AutomaticPostingException error) { return ServiceResult<PackingListPostingPreviewDto>.Fail(error.Code, error.Message); }
    }

    /// <summary>正式反向入口先授权和验证，再由公共执行器独占事务及回执。</summary>
    public async Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid,
        ReversePackingListCommand request, CancellationToken token)
    {
        try
        {
            return await pipeline.ExecuteAsync<ReversePackingListCommand, PackingListPostingCommandResult>(request,
                request?.CommandId, "Shipping.PackingList.Reverse", async ct =>
                {
                    if (!user.IsAuthenticated || user.UserId is not { } actor
                        || !await permissions.HasPermissionAsync(actor, PermissionKeys.ModuleShippingAccess, ct)
                        || !await permissions.HasPermissionAsync(actor, PermissionKeys.PackingListReverse, ct))
                        return ServiceResult<PackingListPostingCommandResult>.Fail(ServiceErrorCodes.Forbidden, "没有当前装箱单操作权限。");
                    return null;
                }, async ct =>
                {
                    try
                    {
                        var outcome = await ExecuteReverseCommandAsync(new(uid, request!.CommandId, request.ExpectedVersion,
                            request.BusinessDate, request.Reason, request.PreviewToken), ct);
                        var dto = new PackingListPostingCommandResult { Status = outcome.AlreadyApplied ? "AlreadyApplied" : "Success",
                            ExecutionId = outcome.ExecutionId, Detail = outcome.Detail, Documents = outcome.Documents };
                        return ServiceResult<CommandExecutionResult<PackingListPostingCommandResult>>.Ok(new(dto, outcome.AlreadyApplied));
                    }
                    catch (PackingListPostingFailure error)
                    { return ServiceResult<CommandExecutionResult<PackingListPostingCommandResult>>.Fail(error.Code, error.Message); }
                    catch (AutomaticPostingException error)
                    { return ServiceResult<CommandExecutionResult<PackingListPostingCommandResult>>.Fail(error.Code, error.Message); }
                }, token);
        }
        catch (CommandValidationException error)
        { return ServiceResult<PackingListPostingCommandResult>.Fail("INVALID_COMMAND", error.Message); }
    }

    /// <inheritdoc/>
    internal async Task<PackingListPostingPreviewBuilder.Preview> ReversePreviewCoreAsync(Guid uid, int expectedVersion,
        DateTime businessDate, string reason, CancellationToken token)
    {
        await RequireReversePermissionAsync(token);
        ValidateReverse(uid, expectedVersion, businessDate, reason);
        if (!await executions.IsSchemaReadyAsync(token)) throw new PackingListPostingFailure("SCHEMA_NOT_READY", "执行证据表尚未部署。");
        var facts = await executions.ReadReverseFactsAsync(uid, token);
        CheckVersion(facts.Head, expectedVersion);
        return new PackingListReversePreviewBuilder().Build(facts, businessDate, reason);
    }

    /// <inheritdoc/>
    internal async Task<PackingListPostOutcome> ExecuteReverseCommandAsync(PackingListReverseRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequireReversePermissionAsync(token);
        ValidateReverse(request.PackingListId, request.ExpectedVersion, request.BusinessDate, request.Reason);
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.PreviewToken))
            throw new PackingListPostingFailure("INVALID_COMMAND", "缺少反向命令身份或预览令牌。");
        if (context.Session.Transaction != null) throw new PackingListPostingFailure("TRANSACTION_EXISTS", "不能接管已有事务。");
        if (!await executions.IsSchemaReadyAsync(token)) throw new PackingListPostingFailure("SCHEMA_NOT_READY", "执行证据表尚未部署。");
        var reason = request.Reason.Trim();
        var fingerprint = Hash(JsonSerializer.Serialize(new { Operation = "Reverse", User = user.UserId,
            request.PackingListId, request.CommandId, request.ExpectedVersion, Date = request.BusinessDate.Date, Reason = reason, request.PreviewToken }));
        // 重放只依赖持久化结果；跳过当前库存探测，但仍在命令锁内核对身份和请求指纹。
        var replayCandidate = await executions.FindCommandAsync(request.CommandId, token);
        var observed = replayCandidate == null ? await packingLists.GetDetailAsync(request.PackingListId, token) : null;
        var sourceIds = observed?.Lines.Select(x => x.GrossDemandId).Distinct().Order().ToArray() ?? [];
        // 事务外探测原执行资源；事务内取得更新锁前不读取余额和原库存单据。
        var candidateFacts = replayCandidate == null ? await executions.ReadReverseFactsAsync(request.PackingListId, token) : null;
        try
        {
        var executed = await executor.ExecuteAsync(request.CommandId, "Shipping.PackingList.Reverse", request.PackingListId,
            new { request.ExpectedVersion, Date = request.BusinessDate.Date, Reason = reason, request.PreviewToken }, ct => ReverseInTransactionAsync(request, candidateFacts, sourceIds, reason, fingerprint, ct), token, IsolationLevel.Serializable);
        executed.Value.AlreadyApplied = executed.IsReplay;
        return executed.Value;
        }
        catch (BusinessCommandException error) { throw new PackingListPostingFailure(error.Code, error.Message); }
    }

    /// <summary>在公共执行器事务内依据原执行事实锁定、反向自动库存、追加证据并更新状态。</summary>
    private async Task<PackingListPostOutcome> ReverseInTransactionAsync(PackingListReverseRequest request,
        PackingListReverseFacts? candidateFacts, Guid[] sourceIds, string reason, string fingerprint, CancellationToken token)
    {
            if (candidateFacts == null)
                throw new PackingListPostingFailure("EXECUTION_INVALID", "已观察到的原反向记录缺失，不能重新执行库存操作。");
            await factsRepository.LockSourcesAsync(sourceIds, token);
            var head = await packingLists.LockHeaderAsync(request.PackingListId, false, token);
            CheckVersion(head, request.ExpectedVersion);
            var physical = await packingLists.LockLinesAsync(request.PackingListId, token);
            if (physical.Any(x => !sourceIds.Contains(x.GrossDemandId)))
                throw new PackingListPostingFailure("VERSION_CONFLICT", "箱单来源集合已变化，请刷新。");
            var facts = candidateFacts;
            var preview = new PackingListReversePreviewBuilder().Build(facts, request.BusinessDate, reason);
            CheckPreview(preview, request.PreviewToken);
            var outCommand = ReverseCommand(false);
            var inCommand = ReverseCommand(true);
            var inventoryEvidence = facts.Evidence.Where(x => x.ActionKind != "Delivery").ToArray();
            if (inventoryEvidence.Length > 0)
            {
                await scope.PrepareAsync(new AutomaticPostingResources { CorrelationId = request.CommandId,
                    PackingListId = request.PackingListId,
                    CommandIds = new[] { outCommand, inCommand }.OfType<AutomaticReverseRequest>().Select(x => x.CommandId).Order().ToArray(),
                    InboundDocumentIds = facts.InboundHeads.Select(x => x.Uid).Order().ToArray(),
                    OutboundDocumentIds = facts.OutboundHeads.Select(x => x.Uid).Order().ToArray(),
                    PurchaseLineIds = facts.InboundLines.Select(x => x.SourceLineId).Distinct().Order().ToArray(),
                    LedgerIds = inventoryEvidence.Select(x => x.LedgerId!.Value).Order().ToArray(),
                    StockKeys = inventoryEvidence.Select(x => new AutomaticStockKey(x.WarehouseSiteId!.Value, x.ProductId!.Value))
                        .Distinct().OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.ProductId).ToArray() }, token);
            }
            // 无库存交付也必须重读；原执行、余额或候选资源变化时退出，不临时追加锁。
            facts = await executions.ReadReverseFactsAsync(request.PackingListId, token);
            preview = new PackingListReversePreviewBuilder().Build(facts, request.BusinessDate, reason);
            CheckPreview(preview, request.PreviewToken);
            // 不允许调换顺序：必须先恢复原出库库存，再撤销本箱单的自动收货。
            var issued = outCommand == null ? new AutomaticPostingResult() : await outbound.ReverseAsync(outCommand, token);
            var received = inCommand == null ? new AutomaticPostingResult() : await inbound.ReverseAsync(inCommand, token);
            var executionId = PackingListAutomaticCommandBuilder.DeriveId(request.CommandId, "execution");
            var evidence = CreateReverseEvidence(executionId, facts, received, issued);
            head!.ReverseCommandId = request.CommandId;
            head.ReversedDate = DateTime.Now;
            head.ReversedBy = user.UserName;
            head.ReverseReason = reason;
            await executions.MarkAsync(head, request.ExpectedVersion, true, token);
            var result = new PackingListPostOutcome { ExecutionId = executionId,
                Documents = DocumentSummaries(received, issued),
                Detail = await packingLists.GetDetailAsync(head.Uid, token),
                InboundDocumentIds = received.Documents.Select(x => x.DocumentId).ToArray(),
                OutboundDocumentIds = issued.Documents.Select(x => x.DocumentId).ToArray() };
            if (result.Detail == null) throw new PackingListPostingFailure("RESULT_MISSING", "反向后的箱单详情不可读取。");
            var resultJson = JsonSerializer.Serialize(result);
            await executions.AppendAsync(new PackingListPostingExecution { Uid = executionId,
                PackingListHeadId = head.Uid, PackingListNo = head.PackingListNo, CommandId = request.CommandId,
                Operation = 2, OriginalExecutionId = facts.Executions.Single().Uid, BusinessDate = request.BusinessDate.Date,
                ExpectedVersion = request.ExpectedVersion, PreviewToken = request.PreviewToken,
                RequestFingerprint = fingerprint, Reason = reason, ResultJson = resultJson }, evidence, token);
            if (await logs.InsertAsync(new BusinessOperationLog { CommandId = request.CommandId, CorrelationId = request.CommandId.ToString("N"),
                Module = "PackingList", AggregateType = nameof(PackingListHead), AggregateUid = head.Uid,
                AggregateNoSnapshot = head.PackingListNo, OperationType = "Reversed", OperationName = "装箱单整单反过账",
                Reason = reason, ChangedFieldsJson = JsonSerializer.Serialize(new { Fingerprint = fingerprint }), RelatedObjectsJson = resultJson,
                UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now,
                Summary = "先恢复全部出库、再撤销本箱单自动入库；原事实保留。" }) != 1)
                throw new PackingListPostingFailure("LOG_FAILED", "反向日志写入失败。");
            if (context.Session.Transaction is null) throw new PackingListPostingFailure("TRANSACTION_ABORTED", "内部反向已终止外层事务。");
            return result;

            AutomaticReverseRequest? ReverseCommand(bool isInbound)
            {
                var docs = isInbound ? facts.InboundHeads.Select(x => new AutomaticReverseDocument(x.Uid, x.Version)).ToArray()
                    : facts.OutboundHeads.Select(x => new AutomaticReverseDocument(x.Uid, x.Version)).ToArray();
                return docs.Length == 0 ? null : new AutomaticReverseRequest { CommandId = PackingListAutomaticCommandBuilder.DeriveId(request.CommandId,
                    isInbound ? "reverse-inbound" : "reverse-outbound"), CorrelationId = request.CommandId, PackingListId = request.PackingListId,
                    BusinessDate = request.BusinessDate.Date, Reason = reason, Documents = docs.OrderBy(x => x.DocumentId).ToArray() };
            }

    }

    /// <summary>逐条保存反向实际流水及原证据引用；不更新原执行明细。</summary>
    internal static IReadOnlyList<PackingListPostingExecutionLine> CreateReverseEvidence(Guid executionId, PackingListReverseFacts facts,
        AutomaticPostingResult received, AutomaticPostingResult issued)
    {
        var results = received.Documents.Concat(issued.Documents).SelectMany(d => d.Lines.Select(l => (Document: d, Line: l))).ToArray();
        var originalInventory = facts.Evidence.Where(x => x.ActionKind != "Delivery").ToArray();
        // 每条原流水必须获得独立的新流水；不能把另一条原流水或重复回执伪装为反向结果。
        var originalLedgerIds = originalInventory.Select(x => x.LedgerId).ToHashSet();
        if (results.Length != originalInventory.Length
            || results.Select(x => (x.Document.DocumentId, x.Line.DocumentLineId)).Distinct().Count() != results.Length
            || results.Select(x => x.Line.LedgerId).Distinct().Count() != results.Length)
            throw new PackingListPostingFailure("EXECUTION_INVALID", "反向返回的原单明细不完整或重复。");
        var output = new List<PackingListPostingExecutionLine>();
        foreach (var original in facts.Evidence.OrderBy(x => x.LineNumber))
        {
            var clone = JsonSerializer.Deserialize<PackingListPostingExecutionLine>(JsonSerializer.Serialize(original))!;
            clone.Uid = PackingListAutomaticCommandBuilder.DeriveId(executionId, $"original:{original.Uid:N}");
            clone.ExecutionId = executionId; clone.OriginalExecutionLineId = original.Uid;
            clone.Version = 1; clone.CreatedBy = clone.UpdatedBy = null; clone.CreatedDate = clone.UpdatedDate = null;
            if (original.ActionKind != "Delivery")
            {
                var actual = results.SingleOrDefault(x => x.Document.DocumentId == original.DocumentId && x.Line.DocumentLineId == original.DocumentLineId);
                if (actual.Document == null || actual.Document.PostingStatus != 8
                    || actual.Document.Version <= original.DocumentVersion
                    || actual.Line.RequestLineId != original.DocumentLineId
                    || actual.Line.LedgerId == Guid.Empty || originalLedgerIds.Contains(actual.Line.LedgerId))
                    throw new PackingListPostingFailure("EXECUTION_INVALID", "缺少实际反向流水或单据状态异常。");
                clone.DocumentVersion = actual.Document.Version;
                clone.LedgerId = actual.Line.LedgerId;
            }
            output.Add(clone);
        }
        return output;
    }

    /// <summary>独立反过账权限，不通过普通库存权限绕过。</summary>
    private async Task RequireReversePermissionAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id || !await permissions.HasPermissionAsync(id, SwiftExport.Contracts.DTOs.Auth.PermissionKeys.PackingListReverse, token))
            throw new PackingListPostingFailure("FORBIDDEN", "没有装箱单反过账权限。");
    }
    /// <summary>原因有明确长度限制，日期与 InvoiceDate 独立。</summary>
    private static void ValidateReverse(Guid uid, int version, DateTime date, string reason)
    {
        Validate(uid, version, date);
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length > 2000)
            throw new PackingListPostingFailure("INVALID_COMMAND", "反向原因必须填写且不能超过 2000 字符。");
    }

    /// <summary>反向命令身份和业务日期必须有效。</summary>
    private static void Validate(Guid uid, int version, DateTime date)
    {
        if (uid == Guid.Empty || version <= 0 || date == default)
            throw new PackingListPostingFailure("INVALID_COMMAND", "箱单身份、版本或业务日期无效。");
    }

    /// <summary>在读取反向事实前拒绝已经变化的箱单版本。</summary>
    private static void CheckVersion(PackingListHead? head, int version)
    {
        if (head == null) throw new PackingListPostingFailure("NOT_FOUND", "箱单不存在。");
        if (head.Version != version) throw new PackingListPostingFailure("VERSION_CONFLICT", "箱单已变化，请刷新。");
    }

    /// <summary>原执行证据或令牌变化时，拒绝全部反向写入。</summary>
    private static void CheckPreview(PackingListPostingPreviewBuilder.Preview preview, string expected)
    {
        if (preview.Blockers.Count > 0)
            throw new PackingListPostingFailure(preview.Blockers[0].Code, preview.Blockers[0].Message) { Blockers = preview.Blockers };
        if (!string.Equals(preview.Token, expected, StringComparison.Ordinal))
            throw new PackingListPostingFailure("PREVIEW_STALE", "影响过账的事实已变化，请重新预览。");
    }

    /// <summary>固定原有请求指纹格式，供执行证据和业务审计追溯。</summary>
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>从端口实际结果记录单据方向、状态与版本。</summary>
    private static List<PackingListGeneratedDocumentDto> DocumentSummaries(AutomaticPostingResult received, AutomaticPostingResult issued) =>
        received.Documents.Select(x => (Document: x, Direction: "Inbound"))
            .Concat(issued.Documents.Select(x => (Document: x, Direction: "Outbound")))
            .Select(x => new PackingListGeneratedDocumentDto { Uid = x.Document.DocumentId,
                DocumentNo = x.Document.DocumentNo, Version = x.Document.Version,
                PostingStatus = x.Document.PostingStatus, Direction = x.Direction }).ToList();
}


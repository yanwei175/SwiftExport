using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Application.Commands;
using Mapster;
using SwiftExport.Application.Services.Shipping;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>拥有唯一事务的正式过账编排；执行表未部署时拒绝写入，真实数据库并发仍需测试库验收。</summary>
public sealed partial class PackingListPostingService(ServiceBaseContext context,
    IPackingListDocumentRepository packingLists, IPackingListPostingFactsRepository factsRepository,
    IPackingListExecutionRepository executions, IInboundSourceRepository inboundRepository,
    IAutomaticPostingScope scope, IInboundPostingPort inbound, IOutboundPostingPort outbound,
    IBusinessOperationLogRepository logs, ICurrentUser user, IPermissionEvaluationService permissions,
    BusinessCommandExecutor executor, TypeAdapterConfig mapping, CommandPipeline pipeline)
    : IPackingListPostingService
{
    private const string Permission = SwiftExport.Contracts.DTOs.Auth.PermissionKeys.PackingListPost;

    /// <inheritdoc/>
    internal async Task<PackingListPostingPreviewBuilder.Preview> PreviewCoreAsync(Guid uid, int expectedVersion,
        DateTime businessDate, CancellationToken token)
    {
        await RequirePermissionAsync(token);
        Validate(uid, expectedVersion, businessDate);
        var facts = await factsRepository.ReadAsync(uid, token);
        CheckVersion(facts.Head, expectedVersion);
        return (await ResolveAsync(facts, businessDate, token)).Preview;
    }

    /// <inheritdoc/>
    internal async Task<PackingListPostOutcome> ExecutePostCommandAsync(PackingListPostRequest request, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(request);
        await RequirePermissionAsync(token);
        Validate(request.PackingListId, request.ExpectedVersion, request.BusinessDate);
        if (request.CommandId == Guid.Empty || string.IsNullOrWhiteSpace(request.PreviewToken))
            throw new PackingListPostingFailure("INVALID_COMMAND", "缺少稳定命令身份或预览令牌。");
        if (context.Session.Transaction is not null)
            throw new PackingListPostingFailure("TRANSACTION_EXISTS", "不能接管已有业务事务。");
        if (!await executions.IsSchemaReadyAsync(token))
            throw new PackingListPostingFailure("SCHEMA_NOT_READY", "执行证据表尚未部署。");
        var fingerprint = Hash(JsonSerializer.Serialize(new { Operation = "Post", User = user.UserId,
            request.PackingListId, request.CommandId, request.ExpectedVersion, Date = request.BusinessDate.Date, request.PreviewToken }));
        // 已持久化结果不依赖当前需求或库存；这里只决定是否跳过探测，事务内仍锁命令并重新核验。
        var replayCandidate = await executions.FindCommandAsync(request.CommandId, token);
        var observed = replayCandidate == null ? await packingLists.GetDetailAsync(request.PackingListId, token) : null;
        var sourceIds = observed?.Lines.Select(x => x.GrossDemandId).OfType<Guid>().Distinct().Order().ToArray() ?? [];
        // 事务外只探测候选集合，不占用库存；避免 Serializable 下先持有余额共享锁再升级。
        var candidate = replayCandidate == null
            ? await ResolveAsync(await factsRepository.ReadAsync(request.PackingListId, token), request.BusinessDate, token)
            : null;
        try
        {
        var executed = await executor.ExecuteAsync(request.CommandId, "Shipping.PackingList.Post", request.PackingListId,
            new { request.ExpectedVersion, Date = request.BusinessDate.Date, request.PreviewToken }, ct => PostInTransactionAsync(request, candidate, sourceIds, fingerprint, ct), token, IsolationLevel.Serializable);
        executed.Value.AlreadyApplied = executed.IsReplay;
        return executed.Value;
        }
        catch (BusinessCommandException error) { throw new PackingListPostingFailure(error.Code, error.Message); }
    }

    /// <summary>仅在公共执行器拥有的事务内完成锁定、重验、自动库存、状态、证据与审计。</summary>
    private async Task<PackingListPostOutcome> PostInTransactionAsync(PackingListPostRequest request,
        PackingListPostingFactsResolver.Resolved? candidate, Guid[] sourceIds, string fingerprint, CancellationToken token)
    {
            if (candidate == null)
                throw new PackingListPostingFailure("EXECUTION_INVALID", "已观察到的原执行记录缺失，不能重新执行库存操作。");
            // 与来源退出和草稿保存统一：来源/需求/Link 在箱单之前，不在持有箱单锁后反向获取来源锁。
            await factsRepository.LockSourcesAsync(sourceIds, token);
            var head = await packingLists.LockHeaderAsync(request.PackingListId, false, token);
            CheckVersion(head, request.ExpectedVersion);
            var physical = await packingLists.LockLinesAsync(request.PackingListId, token);
            if (physical.Any(x => !x.GrossDemandId.HasValue || !sourceIds.Contains(x.GrossDemandId.Value)))
                throw new PackingListPostingFailure("VERSION_CONFLICT", "箱单来源集合已变化，请刷新。");
            CheckPreview(candidate.Preview, request.PreviewToken);
            var builder = new PackingListAutomaticCommandBuilder();
            var commands = builder.Build(request.CommandId, head!.Uid, head.PackingListNo, request.BusinessDate, candidate.Preview,
                candidate.Stocks.Select(x => new AutomaticStockKey(x.WarehouseId, x.ProductId)).ToArray(), candidate.PurchaseLineIds);
            if (commands.Resources != null)
                await scope.PrepareAsync(commands.Resources, token);
            // 包括无库存交付在内，均以来源/箱单锁后的事实重算；候选变化退出，不追加资源锁。
            var locked = await factsRepository.ReadAsync(head.Uid, token);
            var resolved = await ResolveAsync(locked, request.BusinessDate, token);
            CheckPreview(resolved.Preview, request.PreviewToken);
            var received = commands.Inbound == null ? new AutomaticPostingResult()
                : await inbound.CreateAndPostAsync(commands.Inbound, token);
            var issued = commands.Outbound == null ? new AutomaticPostingResult()
                : await outbound.CreateAndPostAsync(commands.Outbound, token);
            var executionId = PackingListAutomaticCommandBuilder.DeriveId(request.CommandId, "execution");
            var records = CreateEvidence(executionId, resolved.Preview, commands, received, issued);
            head.PostCommandId = request.CommandId;
            head.PostingDate = request.BusinessDate.Date;
            head.PostedDate = DateTime.Now;
            head.PostedBy = user.UserName;
            await executions.MarkAsync(head, request.ExpectedVersion, false, token);
            var result = new PackingListPostOutcome { ExecutionId = executionId,
                Documents = DocumentSummaries(received, issued),
                Detail = await packingLists.GetDetailAsync(head.Uid, token),
                InboundDocumentIds = received.Documents.Select(x => x.DocumentId).ToArray(),
                OutboundDocumentIds = issued.Documents.Select(x => x.DocumentId).ToArray() };
            if (result.Detail == null) throw new PackingListPostingFailure("RESULT_MISSING", "过账后的箱单详情不可读取。");
            var resultJson = JsonSerializer.Serialize(result);
            await executions.AppendAsync(new PackingListPostingExecution { Uid = executionId,
                PackingListHeadId = head.Uid, PackingListNo = head.PackingListNo, CommandId = request.CommandId,
                Operation = 1, BusinessDate = request.BusinessDate.Date, ExpectedVersion = request.ExpectedVersion,
                PreviewToken = request.PreviewToken, RequestFingerprint = fingerprint, ResultJson = resultJson }, records, token);
            if (await logs.InsertAsync(new BusinessOperationLog { CommandId = request.CommandId, CorrelationId = request.CommandId.ToString("N"),
                Module = "PackingList", AggregateType = nameof(PackingListHead), AggregateUid = head.Uid,
                AggregateNoSnapshot = head.PackingListNo, OperationType = "Posted", OperationName = "装箱单过账",
                ChangedFieldsJson = JsonSerializer.Serialize(new { Fingerprint = fingerprint }), RelatedObjectsJson = resultJson,
                UserUid = user.UserId, UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now,
                Summary = "自动入出库、执行证据和箱单状态在同一事务提交。" }) != 1)
                throw new PackingListPostingFailure("LOG_FAILED", "业务日志写入失败。");
            if (context.Session.Transaction is null)
                throw new PackingListPostingFailure("TRANSACTION_ABORTED", "内部执行已终止外层事务。");
            return result;

    }

    /// <summary>入库模块是净收货权威，仅在有库存来源时读取相关采购容量和历史证明。</summary>
    private async Task<PackingListPostingFactsResolver.Resolved> ResolveAsync(PackingListPostingFacts facts, DateTime date, CancellationToken token)
    {
        var inventorySources = facts.Sources.Where(x => !x.IsNonStandard
            && x.SourceType != SwiftExport.Contracts.SysEnums.StaticEnums.DocumentTypeStaticsEnum.ComplaintReplenishment).Select(x => x.Uid).ToHashSet();
        var roots = facts.Demands.Where(x => inventorySources.Contains(x.GrossDemandId) && x.ParentNetDemandId == null).Select(x => x.Uid).ToHashSet();
        var ids = facts.Links.Where(x => roots.Contains(x.NetDemandId) && !x.IsDeleted && x.AllocatedQty > x.ReleasedQty)
            .Select(x => x.TargetLineId).Distinct().Order().ToArray();
        var capacity = ids.Length == 0 ? [] : await inboundRepository.ReadPostingSourcesAsync(ids, token);
        var unproven = ids.Length > 0 && await inboundRepository.HasUnprovenReceiptAsync(token);
        return new PackingListPostingFactsResolver().Resolve(facts, capacity, date, unproven, token);
    }

    /// <summary>保存每个动作与真实端口返回的单据/流水，不用采购行冒充入库明细。</summary>
    internal static IReadOnlyList<PackingListPostingExecutionLine> CreateEvidence(Guid executionId,
        PackingListPostingPreviewBuilder.Preview preview, PackingListAutomaticCommandBuilder.Commands commands,
        AutomaticPostingResult received, AutomaticPostingResult issued)
    {
        var results = received.Documents.Concat(issued.Documents).SelectMany(d => d.Lines.Select(l => (Document: d, Line: l))).ToArray();
        if (results.Length != commands.LineActions.Count || results.Select(x => x.Line.RequestLineId).Distinct().Count() != results.Length)
            throw new PackingListPostingFailure("EXECUTION_INVALID", "自动单据返回行不完整或重复。");
        var output = new List<PackingListPostingExecutionLine>();
        foreach (var row in preview.Actions.Where(x => x.Kind == "Delivery")) Add(row, null, null);
        foreach (var pair in commands.LineActions.OrderBy(x => x.Key))
        {
            var result = results.SingleOrDefault(x => x.Line.RequestLineId == pair.Key);
            if (result.Document == null || result.Document.DocumentId == Guid.Empty || result.Document.Version <= 0
                || result.Line.LedgerId == Guid.Empty || result.Line.DocumentLineId == Guid.Empty)
                throw new PackingListPostingFailure("EXECUTION_INVALID", "自动单据缺少实际行和流水对应关系。");
            Add(pair.Value, result.Document, result.Line);
        }
        return output;
        void Add(PackingListPostingPreviewBuilder.Action action, AutomaticDocumentResult? document, AutomaticLineResult? line)
        {
            var number = output.Count + 1;
            output.Add(new PackingListPostingExecutionLine { Uid = PackingListAutomaticCommandBuilder.DeriveId(executionId, $"line:{number}"),
                ExecutionId = executionId, LineNumber = number, PackingListLineId = action.PackingListLineId,
                GrossDemandId = action.GrossDemandId, ActionKind = action.Kind, ProductId = action.ProductId, Unit = action.Unit,
                WarehouseSiteId = action.WarehouseId, PurchaseLineId = action.PurchaseLineId, RootNetDemandId = action.RootId,
                ComponentNetDemandId = action.ComponentId, RootQtySnapshot = action.RootQty, ComponentQtySnapshot = action.ComponentQty,
                ShippedQtySnapshot = action.ShippedQty, Qty = action.Qty, DocumentId = document?.DocumentId,
                DocumentNo = document?.DocumentNo, DocumentVersion = document?.Version, DocumentLineId = line?.DocumentLineId, LedgerId = line?.LedgerId });
        }
    }

    /// <summary>独立箱单过账权限，不要求手工入库或出库权限；未知权限配置默认拒绝。</summary>
    private async Task RequirePermissionAsync(CancellationToken token)
    {
        if (!user.IsAuthenticated || user.UserId is not { } id || !await permissions.HasPermissionAsync(id, Permission, token))
            throw new PackingListPostingFailure("FORBIDDEN", "没有装箱单过账权限。");
    }
    /// <summary>命令必填身份和业务日期验证。</summary>
    private static void Validate(Guid uid, int version, DateTime date)
    {
        if (uid == Guid.Empty || version <= 0 || date == default)
            throw new PackingListPostingFailure("INVALID_COMMAND", "箱单身份、版本或业务日期无效。");
    }
    /// <summary>先检查版本，再由预览计算器检查生命周期。</summary>
    private static void CheckVersion(PackingListHead? head, int version)
    {
        if (head == null) throw new PackingListPostingFailure("NOT_FOUND", "箱单不存在。");
        if (head.Version != version) throw new PackingListPostingFailure("VERSION_CONFLICT", "箱单已变化，请刷新。");
    }
    /// <summary>有阻断或锁后事实变化均不执行任何库存写入。</summary>
    private static void CheckPreview(PackingListPostingPreviewBuilder.Preview preview, string expected)
    {
        if (preview.Blockers.Count > 0) throw new PackingListPostingFailure(preview.Blockers[0].Code, preview.Blockers[0].Message) { Blockers = preview.Blockers };
        if (!string.Equals(preview.Token, expected, StringComparison.Ordinal))
            throw new PackingListPostingFailure("PREVIEW_STALE", "影响过账的事实已变化，请重新预览。");
    }
    /// <summary>固定 UTF-8 SHA-256 请求指纹。</summary>
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    /// <summary>记录实际端口返回单据与方向，不在提交后另读可能已经变化的版本。</summary>
    private static List<SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListGeneratedDocumentDto> DocumentSummaries(
        AutomaticPostingResult received, AutomaticPostingResult issued) =>
        received.Documents.Select(x => (Document: x, Direction: "Inbound"))
            .Concat(issued.Documents.Select(x => (Document: x, Direction: "Outbound")))
            .Select(x => new SwiftExport.Contracts.DTOs.Shipping.PackingLists.PackingListGeneratedDocumentDto
            { Uid = x.Document.DocumentId, DocumentNo = x.Document.DocumentNo, Version = x.Document.Version,
                PostingStatus = x.Document.PostingStatus, Direction = x.Direction }).ToList();
}


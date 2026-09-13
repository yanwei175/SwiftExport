using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Models.Shipping;
using SwiftExport.Infrastructure.Data;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>编排 PL 草稿、来源额度复核、预览及原子过账；不把物料计划当作交付准入。</summary>
public sealed class PackingListWorkspaceService(
    ServiceBaseContext context,
    IPackingListDocumentRepository documents,
    IPackingListPlanningRepository planning,
    IPackingListQueryRepository queries,
    ISourceDeliveryService sourceDelivery,
    PackingListPreviewBuilder previewBuilder,
    IInboundPostingPort inboundPort,
    IOutboundPostingPort outboundPort,
    IInboundDocumentRepository inboundDocuments,
    IOutboundDocumentRepository outboundDocuments,
    IDocumentNoService documentNos,
    IBusinessOperationLogRepository operationLogs,
    ICorrelationContext correlation,
    ICurrentUser currentUser) : IPackingListWorkspaceService
{
    private const string Module = "PackingList";
    private readonly DbSession _session = context.Session;
    private readonly ILogger<PackingListWorkspaceService> _logger = context.LoggerFactory.CreateLogger<PackingListWorkspaceService>();

    public Task<ServiceResult<PagedResponseBase<PackingListListItemDto>>> SearchAsync(PackingListSearchRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => queries.SearchAsync(request ?? new PackingListSearchRequest(), cancellationToken), "查询 PackingList 工作台失败。");

    public Task<ServiceResult<PackingListDetailDto>> GetDetailAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () => await queries.GetDetailAsync(uid, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。"), "加载 PackingList 失败。");

    public Task<ServiceResult<PagedResponseBase<PackingListSourceDto>>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default)
        => ReadAsync(() => planning.SearchSourcesAsync(request ?? new PackingListSourceSearchRequest(), cancellationToken), "查询 PackingList 来源失败。");

    public Task<ServiceResult<PackingListCommandResultDto>> CreateDraftAsync(CreatePackingListDraftCommand command, CancellationToken cancellationToken = default)
    {
        if (command is null || command.CommandId == Guid.Empty || command.Header is null || command.Header.CustomerId == Guid.Empty || command.Lines.Count == 0)
            return Task.FromResult(Blocked("INVALID_COMMAND", "新建命令、客户和来源明细不能为空。"));
        return ExecuteCommandAsync(async () =>
        {
            var existing = await documents.GetByCreateCommandForUpdateAsync(command.CommandId);
            if (existing is not null)
            {
                if (!string.Equals(existing.CreateRequestFingerprint, CreateFingerprint(command), StringComparison.Ordinal))
                    throw new PackingListBusinessException("IDEMPOTENCY_CONFLICT", "CommandId 已用于不同 PackingList 草稿内容。");
                return CommandResult(existing, PackingListCommandStatusEnum.AlreadyApplied, true, "PackingList 草稿已创建。");
            }
            var built = await BuildDocumentAsync(command.Header, command.Lines, null, command.CommandId);
            built.Document.CreateRequestFingerprint = CreateFingerprint(command);
            if (await documents.InsertAsync(built.Document, built.Lines) != 1 + built.Lines.Count)
                throw new PackingListBusinessException("WRITE_FAILED", "PackingList 草稿写入失败。");
            await WriteLogAsync(command.CommandId, built.Document, "PackingList.DraftCreated", "PackingList 草稿已创建", null);
            return CommandResult(built.Document, PackingListCommandStatusEnum.Success, false, "PackingList 草稿已创建。", built.Lines);
        }, cancellationToken);
    }

    public Task<ServiceResult<PackingListCommandResultDto>> UpdateDraftAsync(Guid uid, UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || command.Header is null || command.Lines.Count == 0)
            return Task.FromResult(Blocked("INVALID_COMMAND", "更新命令、版本、客户和来源明细不能为空。"));
        return ExecuteCommandAsync(async () =>
        {
            var current = await documents.GetForUpdateAsync(uid) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            RequireDraft(current, command.ExpectedVersion);
            var oldLines = await documents.GetLinesForUpdateAsync(uid);
            var built = await BuildDocumentAsync(command.Header, command.Lines, current, current.CreateCommandId, oldLines);
            if (await documents.UpdateDraftAsync(built.Document, built.Lines, command.ExpectedVersion) <= 0)
                throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 草稿保存发生并发冲突。", PackingListCommandStatusEnum.VersionConflict);
            built.Document.Version++;
            await WriteLogAsync(command.CommandId, built.Document, "PackingList.DraftUpdated", "PackingList 草稿已更新", null);
            return CommandResult(built.Document, PackingListCommandStatusEnum.Success, false, "PackingList 草稿已更新。", built.Lines);
        }, cancellationToken);
    }

    public Task<ServiceResult<PackingListCommandResultDto>> VoidAsync(Guid uid, PackingListVoidCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.Reason))
            return Task.FromResult(Blocked("INVALID_COMMAND", "作废命令、版本和原因不能为空。"));
        return ExecuteCommandAsync(async () =>
        {
            var document = await documents.GetForUpdateAsync(uid) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            RequireDraft(document, command.ExpectedVersion);
            if (await documents.SetVoidedAsync(uid, command.ExpectedVersion, currentUser.UserName) != 1)
                throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 作废发生并发冲突。", PackingListCommandStatusEnum.VersionConflict);
            document.LifecycleStatus = PackingListLifecycleStatusEnum.Voided; document.Version++;
            await WriteLogAsync(command.CommandId, document, "PackingList.Voided", "PackingList 已作废", command.Reason);
            return CommandResult(document, PackingListCommandStatusEnum.Success, false, "PackingList 已作废。");
        }, cancellationToken);
    }

    public Task<ServiceResult<PackingListPreviewDto>> PostPreviewAsync(Guid uid, PackingListPreviewCommand command, CancellationToken cancellationToken = default)
        => ReadAsync(async () =>
        {
            var snapshot = await planning.LoadSnapshotAsync(uid, false, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            RequireDraft(snapshot.Document, command?.ExpectedVersion ?? 0);
            return previewBuilder.Build(snapshot);
        }, "生成 PackingList 过账预览失败。");

    public Task<ServiceResult<PackingListCommandResultDto>> PostAsync(Guid uid, PackingListPostCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.PreviewToken))
            return Task.FromResult(Blocked("INVALID_COMMAND", "过账命令、版本和 PreviewToken 不能为空。"));
        return ExecuteCommandAsync(async () =>
        {
            var applied = await documents.GetByPostCommandForUpdateAsync(command.CommandId);
            if (applied is not null)
            {
                if (applied.Uid != uid || !string.Equals(applied.PostPreviewToken, command.PreviewToken, StringComparison.OrdinalIgnoreCase))
                    throw new PackingListBusinessException("IDEMPOTENCY_CONFLICT", "CommandId 已用于其他 PackingList 或不同过账内容。");
                return CommandResult(applied, PackingListCommandStatusEnum.AlreadyApplied, true, "PackingList 已过账。", generated: await GeneratedDocumentsAsync(uid));
            }
            var snapshot = await planning.LoadSnapshotAsync(uid, true, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            RequireDraft(snapshot.Document, command.ExpectedVersion);
            await VerifySourceDeliveryAsync(snapshot, true, cancellationToken);
            // 来源要求已按稳定 Guid 顺序锁定后重新读取供货与库存事实，避免首次预览快照覆盖并发变化。
            snapshot = await planning.LoadSnapshotAsync(uid, true, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            RequireDraft(snapshot.Document, command.ExpectedVersion);
            var preview = previewBuilder.Build(snapshot);
            if (!string.Equals(preview.PreviewToken, command.PreviewToken, StringComparison.OrdinalIgnoreCase))
                throw new PackingListBusinessException("PREVIEW_STALE", "预览已失效，请重新预览。", PackingListCommandStatusEnum.VersionConflict);
            if (preview.HasBlockers)
                throw new PackingListBusinessException(preview.Blockers[0].Code, string.Join("；", preview.Blockers.Select(x => x.Message)));
            var requiredWarnings = preview.Warnings.Where(x => x.RequiresConfirmation).Select(x => x.Code).Distinct(StringComparer.Ordinal).ToList();
            if (requiredWarnings.Except(command.ConfirmedWarningCodes ?? [], StringComparer.Ordinal).Any())
                throw new PackingListBusinessException("WARNING_CONFIRMATION_REQUIRED", "存在尚未确认的强提示。");

            var autoInbound = new List<PackingListPostingPortLine>();
            var finalOutbound = new List<PackingListPostingPortLine>();
            var consumption = new List<PackingListPostingPortLine>();
            foreach (var fact in snapshot.Lines.OrderBy(x => x.Line.Uid))
            {
                var previewLine = preview.Lines.Single(x => x.ContributionId == fact.Line.Uid);
                if (previewLine.AutoInboundQty > 0)
                    autoInbound.Add(ToPortLine(fact.Line, previewLine.AutoInboundQty, PostingPortIds.Create(command.CommandId, fact.Line.Uid, "AUTO_INBOUND")));
                finalOutbound.Add(ToPortLine(fact.Line, previewLine.FinalOutboundQty, PostingPortIds.Create(command.CommandId, fact.Line.Uid, "FINAL_OUTBOUND")));
                var remaining = fact.PlannedQty - fact.ShippedNetQty;
                foreach (var supplied in fact.CustomerSuppliedItems)
                {
                    var target = fact.Line.Quantity >= remaining ? supplied.PlannedQty : Round4(supplied.PlannedQty * Math.Min(fact.ShippedNetQty + fact.Line.Quantity, fact.PlannedQty) / supplied.ParentPlannedQty);
                    var quantity = Math.Max(target - supplied.HistoricalConsumptionQty, 0m);
                    if (quantity <= 0) continue;
                    consumption.Add(new PackingListPostingPortLine
                    {
                        CommandItemId = PostingPortIds.Create(command.CommandId, supplied.AllocationId, "CUSTOMER_SUPPLIED_CONSUMPTION"),
                        ContributionId = fact.Line.Uid, NetDemandAllocationId = supplied.AllocationId, GrossDemandId = fact.Line.GrossDemandId,
                        WarehouseSiteId = fact.Line.WarehouseSiteId, WarehouseCode = fact.Line.WarehouseCodeSnapshot, WarehouseName = fact.Line.WarehouseNameSnapshot,
                        SupplierId = fact.Line.SourceSupplierId, SupplierName = fact.Line.SourceSupplierNameSnapshot,
                        ProductId = supplied.ProductId, Sku = supplied.Sku, Unit = supplied.Unit, SalesOrderNo = fact.Line.SalesOrderNoSnapshot, Quantity = quantity
                    });
                }
            }
            var generated = new List<PackingListGeneratedDocumentDto>();
            generated.AddRange(await inboundPort.PostAutoReceiptsAsync(uid, snapshot.Document.PackingListNo, command.CommandId, snapshot.Document.ShipmentDate, autoInbound, cancellationToken));
            generated.AddRange(await outboundPort.PostAsync(uid, snapshot.Document.PackingListNo, command.CommandId, snapshot.Document.ShipmentDate, finalOutbound, consumption, cancellationToken));
            if (await documents.SetPostedAsync(uid, command.ExpectedVersion, command.CommandId, command.PreviewToken, DateTime.Now, currentUser.UserName) != 1)
                throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 过账发生并发冲突。", PackingListCommandStatusEnum.VersionConflict);
            snapshot.Document.PostingStatus = PackingListPostingStatusEnum.Posted; snapshot.Document.PostCommandId = command.CommandId; snapshot.Document.PostPreviewToken = command.PreviewToken; snapshot.Document.Version++;
            await WriteLogAsync(command.CommandId, snapshot.Document, "PackingList.Posted", "PackingList 已过账", null);
            return CommandResult(snapshot.Document, PackingListCommandStatusEnum.Success, false, "PackingList 已过账。", snapshot.Lines.Select(x => x.Line), generated);
        }, cancellationToken);
    }

    public Task<ServiceResult<PackingListPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReverseCommand command, CancellationToken cancellationToken = default)
        => ReadAsync(() => BuildReversePreviewAsync(uid, command?.ExpectedVersion ?? 0, false, cancellationToken), "生成 PackingList 反过账预览失败。");

    public Task<ServiceResult<PackingListCommandResultDto>> ReverseAsync(Guid uid, PackingListReverseCommand command, CancellationToken cancellationToken = default)
    {
        if (uid == Guid.Empty || command is null || command.CommandId == Guid.Empty || command.ExpectedVersion <= 0 || string.IsNullOrWhiteSpace(command.PreviewToken) || string.IsNullOrWhiteSpace(command.Reason))
            return Task.FromResult(Blocked("INVALID_COMMAND", "反过账命令、版本、PreviewToken 和原因不能为空。"));
        return ExecuteCommandAsync(async () =>
        {
            var applied = await documents.GetByReverseCommandForUpdateAsync(command.CommandId);
            if (applied is not null)
            {
                if (applied.Uid != uid || !string.Equals(applied.ReversePreviewToken, command.PreviewToken, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(Normalize(applied.ReverseReason, 1000), Normalize(command.Reason, 1000), StringComparison.Ordinal))
                    throw new PackingListBusinessException("IDEMPOTENCY_CONFLICT", "CommandId 已用于其他 PackingList 或不同反过账内容。");
                return CommandResult(applied, PackingListCommandStatusEnum.AlreadyApplied, true, "PackingList 已反过账。");
            }
            var document = await documents.GetForUpdateAsync(uid) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            if (document.Version != command.ExpectedVersion || document.PostingStatus != PackingListPostingStatusEnum.Posted)
                throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 状态或版本不允许反过账。", PackingListCommandStatusEnum.VersionConflict);
            var preview = await BuildReversePreviewAsync(uid, command.ExpectedVersion, true, cancellationToken);
            if (!string.Equals(preview.PreviewToken, command.PreviewToken, StringComparison.OrdinalIgnoreCase))
                throw new PackingListBusinessException("PREVIEW_STALE", "反过账预览已失效，请重新预览。", PackingListCommandStatusEnum.VersionConflict);
            if (preview.HasBlockers) throw new PackingListBusinessException(preview.Blockers[0].Code, string.Join("；", preview.Blockers.Select(x => x.Message)));
            await outboundPort.ReverseAsync(uid, command.CommandId, Normalize(command.Reason, 1000)!, cancellationToken);
            await inboundPort.ReverseAutoReceiptsAsync(uid, command.CommandId, Normalize(command.Reason, 1000)!, cancellationToken);
            if (await documents.SetReversedAsync(uid, command.ExpectedVersion, command.CommandId, command.PreviewToken, DateTime.Now, currentUser.UserName, Normalize(command.Reason, 1000)!) != 1)
                throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 反过账发生并发冲突。", PackingListCommandStatusEnum.VersionConflict);
            document.PostingStatus = PackingListPostingStatusEnum.Reversed; document.ReverseCommandId = command.CommandId; document.ReversePreviewToken = command.PreviewToken; document.Version++;
            await WriteLogAsync(command.CommandId, document, "PackingList.Reversed", "PackingList 已反过账", command.Reason);
            return CommandResult(document, PackingListCommandStatusEnum.Success, false, "PackingList 已反过账。");
        }, cancellationToken);
    }

    public Task<ServiceResult<PackingListTraceDto>> GetTraceAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () => await queries.GetTraceAsync(uid, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。"), "加载 PackingList 追溯失败。");

    public Task<ServiceResult<PackingListExportDto>> GetExportAsync(Guid uid, CancellationToken cancellationToken = default)
        => ReadAsync(async () =>
        {
            var detail = await queries.GetDetailAsync(uid, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
            return new PackingListExportDto
            {
                Header = detail.Header, SourceTrace = detail.Lines,
                ProductSummary = detail.Lines.GroupBy(x => new { x.ProductId, x.Sku, x.CustomerSku, x.Description, x.Unit }).Select(x => new PackingListProductSummaryDto
                {
                    ProductId = x.Key.ProductId, Sku = x.Key.Sku, CustomerSku = x.Key.CustomerSku, Description = x.Key.Description, Unit = x.Key.Unit,
                    Quantity = x.Sum(y => y.Quantity), PackageCount = x.Sum(y => y.PackageCount ?? 0), NetWeight = x.Sum(y => y.NetWeight ?? 0),
                    GrossWeight = x.Sum(y => y.GrossWeight ?? 0), Volume = x.Sum(y => y.Volume ?? 0)
                }).ToList()
            };
        }, "生成 PackingList 导出数据失败。");

    private async Task<(PackingListDocument Document, List<PackingListContributionLine> Lines)> BuildDocumentAsync(
        PackingListHeaderCommand header, IReadOnlyList<PackingListContributionCommand> commands, PackingListDocument? current, Guid createCommandId,
        IReadOnlyList<PackingListContributionLine>? oldLines = null)
    {
        if (commands.Any(x => x.GrossDemandId == Guid.Empty)
            || commands.Select(x => x.GrossDemandId).Distinct().Count() != commands.Count)
            throw new PackingListBusinessException("DUPLICATE_SOURCE_DEMAND", "同一 PackingList 中来源需求不能为空或重复。");
        await sourceDelivery.VerifyForUpdateAsync(commands.Select(x => new SourceDeliveryClaim
        {
            GrossDemandId = x.GrossDemandId,
            ExpectedDemandVersion = x.ExpectedDemandVersion,
            ExpectedSourceVersion = x.ExpectedSourceVersion,
            Quantity = x.Quantity
        }).ToList(), false);
        var sources = new List<PackingListSourceDto>();
        foreach (var command in commands)
        {
            var source = await planning.GetSourceForUpdateAsync(command.GrossDemandId, command.WarehouseSiteId) ?? throw new PackingListBusinessException("SOURCE_INACTIVE", "PackingList 来源不存在或已失效。");
            if (source.DemandVersion != command.ExpectedDemandVersion || source.SourceVersion != command.ExpectedSourceVersion
                || command.Quantity <= 0 || command.Quantity > command.ExpectedRemainingQty || command.Quantity > source.ShippableRemainingQty)
                throw new PackingListBusinessException("SOURCE_CHANGED", $"{source.Sku} 的可发货剩余量已变化。");
            if (source.CustomerId != header.CustomerId) throw new PackingListBusinessException("CUSTOMER_MISMATCH", "PackingList 只能包含同一最终客户的来源。");
            sources.Add(source);
        }
        var first = sources[0];
        var no = Normalize(header.PackingListNo, 100);
        if (string.IsNullOrWhiteSpace(no))
        {
            var result = await documentNos.GetNextDocumentNoAsync(new DocumentNoRequest { DocType = "PL", BusinessDate = header.PackingDate == default ? DateTime.Today : header.PackingDate, Operator = currentUser.UserName });
            if (!result.Success || result.Data is null) throw new PackingListBusinessException("DOCUMENT_NO_FAILED", "PackingList 单号生成失败。");
            no = result.Data.DocumentNo;
        }
        var document = current ?? new PackingListDocument { Uid = GuidSequentialGenerator.NewGuid(), CreateCommandId = createCommandId };
        document.PackingListNo = no; document.CustomerId = first.CustomerId; document.CustomerCodeSnapshot = first.CustomerCode; document.CustomerNameSnapshot = first.CustomerName;
        document.PackingDate = header.PackingDate == default ? DateTime.Today : header.PackingDate.Date; document.ShipmentDate = header.ShipmentDate == default ? DateTime.Today : header.ShipmentDate.Date;
        document.Etd = header.Etd; document.Eta = header.Eta; document.InvoiceNo = Normalize(header.InvoiceNo, 100); document.ContainerNo = Normalize(header.ContainerNo, 100);
        document.SealNo = Normalize(header.SealNo, 100); document.ShippingMark = Normalize(header.ShippingMark, 1000); document.DeliveryAddressSnapshot = Normalize(header.DeliveryAddressSnapshot, 1000);
        document.LifecycleStatus = PackingListLifecycleStatusEnum.Active; document.PostingStatus = PackingListPostingStatusEnum.Unposted;
        var oldById = (oldLines ?? []).ToDictionary(x => x.Uid);
        var lines = commands.Select((command, index) =>
        {
            var source = sources[index];
            oldById.TryGetValue(command.Uid ?? Guid.Empty, out var old);
            if (old is not null && command.ExpectedVersion != old.Version) throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 明细已被修改。", PackingListCommandStatusEnum.VersionConflict);
            return new PackingListContributionLine
            {
                Uid = old?.Uid ?? GuidSequentialGenerator.NewGuid(), Version = old?.Version ?? 1, LineNumber = index + 1,
                ProductId = source.ProductId, SkuSnapshot = source.Sku, CustomerSkuSnapshot = source.CustomerSku, DescriptionSnapshot = source.Description, UnitSnapshot = source.Unit,
                Quantity = command.Quantity, PackageCount = command.PackageCount, NetWeight = command.NetWeight, GrossWeight = command.GrossWeight, Volume = command.Volume,
                Marks = Normalize(command.Marks, 500), Note = Normalize(command.Note, 1000), GrossDemandId = source.GrossDemandId, NetDemandId = source.NetDemandId,
                NetDemandAllocationId = source.NetDemandAllocationId, SourceDemandVersionSnapshot = source.DemandVersion, SourceVersionSnapshot = source.SourceVersion,
                DemandExecutionLinkId = source.DemandExecutionLinkId, SalesOrderId = source.SalesOrderId,
                SalesOrderLineId = source.SalesOrderLineId, SalesOrderNoSnapshot = source.SalesOrderNo, SalesOrderLineNumberSnapshot = source.SalesOrderLineNumber,
                SourceSupplierId = source.SourceSupplierId, SourceSupplierNameSnapshot = source.SourceSupplierName, WarehouseSiteId = source.WarehouseSiteId,
                WarehouseCodeSnapshot = source.WarehouseCode, WarehouseNameSnapshot = source.WarehouseName, SnapshotRequiredDate = source.RequiredDate, SupplyType = source.SupplyType,
                CommandItemId = old?.CommandItemId ?? GuidSequentialGenerator.NewGuid()
            };
        }).ToList();
        return (document, lines);
    }

    private async Task<PackingListPreviewDto> BuildReversePreviewAsync(Guid uid, int expectedVersion, bool forUpdate, CancellationToken cancellationToken)
    {
        var snapshot = await planning.LoadSnapshotAsync(uid, forUpdate, cancellationToken) ?? throw new PackingListBusinessException("NOT_FOUND", "PackingList 不存在。");
        if (snapshot.Document.Version != expectedVersion || snapshot.Document.PostingStatus != PackingListPostingStatusEnum.Posted)
            throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 状态或版本不允许反过账。", PackingListCommandStatusEnum.VersionConflict);
        var result = new PackingListPreviewDto { PackingListId = uid, Version = expectedVersion, IsReverse = true };
        if (snapshot.Lines.Count == 0)
            result.Blockers.Add(new PackingListMessageDto { Code = "GENERATED_DOCUMENT_MISSING", Message = "PackingList 缺少可追溯的贡献明细。" });
        var balances = new Dictionary<(Guid Warehouse, Guid Product), (string Sku, string? Name, decimal Qty, int Version)>();
        foreach (var fact in snapshot.Lines)
        {
            balances[(fact.Line.WarehouseSiteId, fact.Line.ProductId)] = (fact.Line.SkuSnapshot, fact.Line.WarehouseNameSnapshot, fact.BalanceQty, fact.BalanceVersion);
            foreach (var supplied in fact.CustomerSuppliedItems) balances[(fact.Line.WarehouseSiteId, supplied.ProductId)] = (supplied.Sku, fact.Line.WarehouseNameSnapshot, supplied.BalanceQty, supplied.BalanceVersion);
        }
        var outbound = (await outboundDocuments.GetByPackingListForUpdateAsync(uid))
            .Where(x => x.DocumentType is OutboundDocumentTypeEnum.FinalCustomerShipment or OutboundDocumentTypeEnum.CustomerSuppliedConsumption)
            .ToList();
        var inbound = (await inboundDocuments.GetByPackingListForUpdateAsync(uid))
            .Where(x => x.DocumentType == InboundDocumentTypeEnum.PackingListAutoReceipt)
            .ToList();
        var tokenParts = new List<string> { uid.ToString("N"), expectedVersion.ToString(CultureInfo.InvariantCulture) };
        foreach (var warehouseId in snapshot.Lines.Select(x => x.Line.WarehouseSiteId).Distinct().OrderBy(x => x))
        {
            if (!outbound.Any(x => x.WarehouseSiteId == warehouseId && x.DocumentType == OutboundDocumentTypeEnum.FinalCustomerShipment))
                result.Blockers.Add(new PackingListMessageDto { Code = "GENERATED_DOCUMENT_MISSING", Message = $"仓库 {warehouseId:N} 缺少最终客户出库单。" });
        }
        foreach (var doc in outbound.OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.Uid))
        {
            tokenParts.Add($"OD:{doc.Uid:N}:{doc.Version}:{(int)doc.PostingStatus}");
            if (doc.PostingStatus != OutboundPostingStatusEnum.Posted) { result.Blockers.Add(new PackingListMessageDto { Code = "GENERATED_DOCUMENT_STATE_INVALID", Message = $"出库单 {doc.DocumentNo} 状态异常。" }); continue; }
            foreach (var ledger in await outboundDocuments.GetLedgersAsync(doc.Uid, cancellationToken))
            {
                var key = (ledger.WarehouseSiteId, ledger.ProductId); if (!balances.TryGetValue(key, out var current)) current = (ledger.Sku, null, ledger.BalanceAfter, 0);
                current.Qty -= ledger.QuantityDelta; balances[key] = current; tokenParts.Add($"O:{ledger.Uid:N}:{ledger.QuantityDelta}");
            }
        }
        foreach (var doc in inbound.OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.Uid))
        {
            tokenParts.Add($"ID:{doc.Uid:N}:{doc.Version}:{(int)doc.PostingStatus}");
            if (doc.PostingStatus != InboundPostingStatusEnum.Posted) { result.Blockers.Add(new PackingListMessageDto { Code = "GENERATED_DOCUMENT_STATE_INVALID", Message = $"入库单 {doc.DocumentNo} 状态异常。" }); continue; }
            foreach (var ledger in await inboundDocuments.GetLedgersAsync(doc.Uid, cancellationToken))
            {
                var key = (ledger.WarehouseSiteId, ledger.ProductId); if (!balances.TryGetValue(key, out var current)) current = (ledger.Sku, null, ledger.BalanceAfter, 0);
                current.Qty -= ledger.QuantityDelta; balances[key] = current; tokenParts.Add($"I:{ledger.Uid:N}:{ledger.QuantityDelta}");
            }
        }
        foreach (var pair in balances.OrderBy(x => x.Key.Warehouse).ThenBy(x => x.Key.Product))
        {
            if (pair.Value.Qty < 0) result.Blockers.Add(new PackingListMessageDto { Code = "REVERSE_NEGATIVE_STOCK", Message = $"{pair.Value.Sku} 反过账后库存将为负。" });
            result.InventoryEffects.Add(new PackingListInventoryEffectDto { WarehouseSiteId = pair.Key.Warehouse, ProductId = pair.Key.Product, Sku = pair.Value.Sku, WarehouseName = pair.Value.Name, BalanceAfter = pair.Value.Qty });
            tokenParts.Add($"B:{pair.Key.Warehouse:N}:{pair.Key.Product:N}:{pair.Value.Version}:{pair.Value.Qty}");
        }
        result.HasBlockers = result.Blockers.Count > 0;
        result.PreviewToken = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", tokenParts))));
        return result;
    }

    /// <summary>在 PL 最外层事务内复核来源版本和合计额度；仅对实际产生客供件消耗的行要求订单池归属。</summary>
    private Task<IReadOnlyDictionary<Guid, SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery.SourceDemandDeliveryItemDto>> VerifySourceDeliveryAsync(
        PackingListPlanningSnapshot snapshot,
        bool requireInventoryEligibility,
        CancellationToken cancellationToken)
        => sourceDelivery.VerifyForUpdateAsync(snapshot.Lines.Select(item => new SourceDeliveryClaim
        {
            GrossDemandId = item.Line.GrossDemandId,
            ExpectedDemandVersion = item.Line.SourceDemandVersionSnapshot,
            ExpectedSourceVersion = item.Line.SourceVersionSnapshot,
            Quantity = item.Line.Quantity,
            RequiresOrderPoolOwnership = RequiresOrderPoolOwnership(item)
        }).ToList(), requireInventoryEligibility, cancellationToken);

    /// <summary>复用预览的累计消耗口径，只在本次确有正数客供件消耗时启用订单池门禁。</summary>
    private static bool RequiresOrderPoolOwnership(PackingListPlanningLineSnapshot fact)
    {
        var remaining = fact.PlannedQty - fact.ShippedNetQty;
        return fact.CustomerSuppliedItems.Any(item =>
        {
            var target = fact.Line.Quantity >= remaining
                ? item.PlannedQty
                : Round4(item.PlannedQty * Math.Min(fact.ShippedNetQty + fact.Line.Quantity, fact.PlannedQty) / item.ParentPlannedQty);
            return target - item.HistoricalConsumptionQty > 0;
        });
    }

    private async Task<List<PackingListGeneratedDocumentDto>> GeneratedDocumentsAsync(Guid uid)
    {
        var result = new List<PackingListGeneratedDocumentDto>();
        result.AddRange((await inboundDocuments.GetByPackingListForUpdateAsync(uid))
            .Where(x => x.DocumentType == InboundDocumentTypeEnum.PackingListAutoReceipt)
            .Select(x => new PackingListGeneratedDocumentDto { DocumentKind = "AutoInbound", Uid = x.Uid, DocumentNo = x.DocumentNo }));
        result.AddRange((await outboundDocuments.GetByPackingListForUpdateAsync(uid))
            .Where(x => x.DocumentType is OutboundDocumentTypeEnum.FinalCustomerShipment or OutboundDocumentTypeEnum.CustomerSuppliedConsumption)
            .Select(x => new PackingListGeneratedDocumentDto { DocumentKind = x.DocumentType == OutboundDocumentTypeEnum.FinalCustomerShipment ? "FinalOutbound" : "CustomerSuppliedConsumption", Uid = x.Uid, DocumentNo = x.DocumentNo }));
        return result;
    }

    private static PackingListPostingPortLine ToPortLine(PackingListContributionLine line, decimal quantity, Guid commandItemId) => new()
    {
        CommandItemId = commandItemId, ContributionId = line.Uid, NetDemandAllocationId = line.NetDemandAllocationId, GrossDemandId = line.GrossDemandId,
        WarehouseSiteId = line.WarehouseSiteId, WarehouseCode = line.WarehouseCodeSnapshot, WarehouseName = line.WarehouseNameSnapshot, SupplierId = line.SourceSupplierId, SupplierName = line.SourceSupplierNameSnapshot,
        ProductId = line.ProductId, Sku = line.SkuSnapshot, Description = line.DescriptionSnapshot, Unit = line.UnitSnapshot, SalesOrderNo = line.SalesOrderNoSnapshot, Quantity = quantity
    };

    private static void RequireDraft(PackingListDocument document, int version)
    {
        if (document.Version != version) throw new PackingListBusinessException("VERSION_CONFLICT", "PackingList 版本已变化。", PackingListCommandStatusEnum.VersionConflict);
        if (document.LifecycleStatus != PackingListLifecycleStatusEnum.Active || document.PostingStatus != PackingListPostingStatusEnum.Unposted)
            throw new PackingListBusinessException("STATE_INVALID", "PackingList 不是可操作草稿。");
    }

    private async Task<ServiceResult<T>> ReadAsync<T>(Func<Task<T>> action, string failure)
    {
        try { return ServiceResult<T>.Ok(await action()); }
        catch (PackingListBusinessException ex) { return ServiceResult<T>.Fail($"{ex.Code}：{ex.Message}"); }
        catch (Exception ex) { _logger.LogError(ex, "{Failure}", failure); return ServiceResult<T>.Fail(failure); }
    }

    private async Task<ServiceResult<PackingListCommandResultDto>> ExecuteCommandAsync(Func<Task<PackingListCommandResultDto>> action, CancellationToken cancellationToken)
    {
        if (_session.Transaction is not null) return Blocked("TRANSACTION_ACTIVE", "当前请求已存在未完成事务。");
        _session.Transaction = _session.Connection.BeginTransaction();
        try { var result = await action(); cancellationToken.ThrowIfCancellationRequested(); _session.Transaction.Commit(); return ServiceResult<PackingListCommandResultDto>.Ok(result); }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { TryRollback(); throw; }
        catch (PackingListBusinessException ex) { TryRollback(); return Blocked(ex.Code, ex.Message, ex.Status); }
        catch (SourceDeliveryException ex) { TryRollback(); return Blocked(ex.Code, ex.Message); }
        catch (InventoryPostingException ex) { TryRollback(); return Blocked("POSTING_BLOCKED", ex.Message); }
        catch (Exception ex) { TryRollback(); _logger.LogError(ex, "PackingList 命令执行失败。"); return Blocked("UNEXPECTED_ERROR", "PackingList 命令执行失败。"); }
        finally { _session.Transaction.Dispose(); _session.Transaction = null; }
    }

    /// <summary>库存内核可能已回滚外层事务；重复回滚不得覆盖首个业务异常。</summary>
    private void TryRollback() { try { _session.Transaction?.Rollback(); } catch { } }

    private async Task WriteLogAsync(Guid commandId, PackingListDocument document, string operationType, string operationName, string? reason)
    {
        if (await operationLogs.GetByCommandAsync(Module, operationType, commandId) is not null) return;
        var log = new BusinessOperationLog
        {
            Uid = GuidSequentialGenerator.NewGuid(), CorrelationId = Normalize(correlation.CorrelationId, 64) ?? commandId.ToString("N"), CommandId = commandId,
            Module = Module, AggregateType = "PackingListDocument", AggregateUid = document.Uid, AggregateNoSnapshot = document.PackingListNo,
            OperationType = operationType, OperationName = operationName, Reason = Normalize(reason, 1000), Summary = operationName,
            UserUid = currentUser.UserId, UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
        };
        if (await operationLogs.InsertAsync(log) != 1) throw new PackingListBusinessException("LOG_WRITE_FAILED", "业务操作日志写入失败。");
    }

    private static ServiceResult<PackingListCommandResultDto> Blocked(string code, string message, PackingListCommandStatusEnum status = PackingListCommandStatusEnum.Blocked)
        => ServiceResult<PackingListCommandResultDto>.Ok(new PackingListCommandResultDto { Status = status, ErrorCode = code, Message = message });

    private static PackingListCommandResultDto CommandResult(PackingListDocument document, PackingListCommandStatusEnum status, bool applied, string message,
        IEnumerable<PackingListContributionLine>? lines = null, IEnumerable<PackingListGeneratedDocumentDto>? generated = null)
        => new()
        {
            Status = status, PackingListId = document.Uid, Version = document.Version, AlreadyApplied = applied, Message = message,
            GeneratedDocuments = generated?.ToList() ?? [], Contributions = lines?.Select(x => new PackingListContributionResultDto { ContributionId = x.Uid, Success = true, Message = message }).ToList() ?? []
        };

    private static decimal Round4(decimal value) => Math.Round(value, 4, MidpointRounding.AwayFromZero);
    private static string CreateFingerprint(CreatePackingListDraftCommand command)
    {
        var h = command.Header;
        var values = new List<string>
        {
            h.CustomerId.ToString("N"), Normalize(h.PackingListNo,100) ?? string.Empty,
            (h.PackingDate == default ? DateTime.Today : h.PackingDate.Date).Ticks.ToString(CultureInfo.InvariantCulture),
            (h.ShipmentDate == default ? DateTime.Today : h.ShipmentDate.Date).Ticks.ToString(CultureInfo.InvariantCulture),
            h.Etd?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty, h.Eta?.Ticks.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            Normalize(h.InvoiceNo,100) ?? string.Empty, Normalize(h.ContainerNo,100) ?? string.Empty, Normalize(h.SealNo,100) ?? string.Empty,
            Normalize(h.ShippingMark,1000) ?? string.Empty, Normalize(h.DeliveryAddressSnapshot,1000) ?? string.Empty
        };
        values.AddRange(command.Lines.OrderBy(x => x.GrossDemandId).Select(x => $"{x.GrossDemandId:N}:{x.WarehouseSiteId}:{x.ExpectedDemandVersion}:{x.ExpectedSourceVersion}:{x.ExpectedRemainingQty.ToString("0.####",CultureInfo.InvariantCulture)}:{x.Quantity.ToString("0.####",CultureInfo.InvariantCulture)}:{x.PackageCount?.ToString("0.####",CultureInfo.InvariantCulture)}:{x.NetWeight?.ToString("0.####",CultureInfo.InvariantCulture)}:{x.GrossWeight?.ToString("0.####",CultureInfo.InvariantCulture)}:{x.Volume?.ToString("0.####",CultureInfo.InvariantCulture)}:{Normalize(x.Marks,500)}:{Normalize(x.Note,1000)}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values))));
    }
    private static string? Normalize(string? value, int max) { var text = value?.Trim(); return string.IsNullOrEmpty(text) ? null : text[..Math.Min(text.Length, max)]; }

    private sealed class PackingListBusinessException(string code, string message, PackingListCommandStatusEnum status = PackingListCommandStatusEnum.Blocked) : Exception(message)
    {
        public string Code { get; } = code;
        public PackingListCommandStatusEnum Status { get; } = status;
    }
}

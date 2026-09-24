using SwiftExport.Application.Services.Inventory.Posting;
using SwiftExport.Core.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Posting;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;

namespace SwiftExport.Application.Services.Inventory.AutomaticDocuments;

/// <summary>箱单自动单据内部执行器；只处理已分配的事实，所有写入加入外层事务。</summary>
public sealed partial class AutomaticPostingPorts(AutomaticPostingScope scope, IAutomaticDocumentRepository repository,
    IInboundDocumentRepository inbound, IInboundSourceRepository sourceRepository, IInboundExecutionReadRepository executionReads, IOutboundDocumentRepository outbound, IOutboundExecutionReadRepository outboundReads, IInventoryPostingService inventory,
    IRepository<WarehouseSite> warehouses, IInventoryPostingRepository stocks, IBusinessOperationLogRepository logs,
    IDocumentNoService numbers, ICurrentUser user) : IInboundPostingPort, IOutboundPostingPort
{
    private const string Module = "AutomaticInventory";

    Task<AutomaticPostingResult> IInboundPostingPort.CreateAndPostAsync(AutomaticPostingRequest request, CancellationToken token) => CreateAsync(request, true, token);
    Task<AutomaticPostingResult> IOutboundPostingPort.CreateAndPostAsync(AutomaticPostingRequest request, CancellationToken token) => CreateAsync(request, false, token);
    Task<AutomaticPostingResult> IInboundPostingPort.ReverseAsync(AutomaticReverseRequest request, CancellationToken token) => ReverseAsync(request, true, token);
    Task<AutomaticPostingResult> IOutboundPostingPort.ReverseAsync(AutomaticReverseRequest request, CancellationToken token) => ReverseAsync(request, false, token);

    /// <summary>先验证批次并检查重放，再取号、写单据、过账和记录完整结果。</summary>
    private async Task<AutomaticPostingResult> CreateAsync(AutomaticPostingRequest submitted, bool isInbound, CancellationToken token)
    {
        try
        {
            RequireUser();
            if (submitted is null) Fail("VALIDATION", "请求不能为空。");
            // 冻结输入，避免 await 期间调用方修改请求形成指纹与写入内容不一致。
            var request = JsonSerializer.Deserialize<AutomaticPostingRequest>(JsonSerializer.Serialize(submitted))!;
            scope.Require(request.CommandId, request.CorrelationId, request.PackingListId);
            Validate(request, isInbound);
            foreach (var group in request.Groups) scope.RequireGroup(group, isInbound);
            var operation = isInbound ? "Inbound.Post" : "Outbound.Post";
            var fingerprint = Fingerprint(new
            {
                UserId = user.UserId, operation, request.CommandId, request.CorrelationId, request.PackingListId,
                request.PackingListNo, request.BusinessDate,
                Groups = request.Groups.OrderBy(x => x.GroupId).Select(g => new
                { g.GroupId, g.WarehouseSiteId, g.OutboundType, Lines = g.Lines.OrderBy(x => x.LineId) })
            });
            var prior = await ReplayAsync(request.CommandId, operation, fingerprint);
            if (prior is not null) return prior;
            if (isInbound) await ValidatePurchaseCapacityAsync(request, token);
            var result = new AutomaticPostingResult();
            foreach (var group in request.Groups.OrderBy(x => x.GroupId))
            {
                token.ThrowIfCancellationRequested();
                var warehouse = await warehouses.GetByIdAsync(group.WarehouseSiteId, true);
                if (warehouse is null || warehouse.IsDeleted || !warehouse.IsActive) Fail("WAREHOUSE_INVALID", "仓库不可用于新库存事实。", group.GroupId);
                if (isInbound)
                {
                    if (await inbound.LockHeaderAsync(group.GroupId, token) is not null) Fail("COMMAND_CONFLICT", "自动单据身份已存在且没有本命令结果。", group.GroupId);
                }
                else if (await outbound.LockHeaderAsync(group.GroupId, token) is not null) Fail("COMMAND_CONFLICT", "自动单据身份已存在且没有本命令结果。", group.GroupId);
                var number = await numbers.GetNextDocumentNoAsync(new()
                {
                    DocType = isInbound ? DocumentTypeStaticsEnum.InboundDocument : DocumentTypeStaticsEnum.OutboundDocument,
                    BusinessDate = request.BusinessDate, Operator = user.UserName
                });
                if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo)) Fail("NUMBER_FAILED", number.Message ?? "自动单据取号失败。");
                var documentNo = number.Data!.DocumentNo;
                var items = new List<InventoryPostingItem>();
                var inLines = new List<InboundDocumentLine>();
                var outLines = new List<OutboundDocumentLine>();
                foreach (var line in group.Lines.OrderBy(x => x.LineId))
                {
                    var product = await stocks.GetProductForPostingAsync(line.ProductId);
                    if (product is null || !SameUnit(product.Unit, line.Unit)) Fail("PRODUCT_UNIT_INVALID", "产品不存在或单位不匹配。", line.LineId);
                    var description = product!.DescriptionEn ?? product.DescriptionZh;
                    inLines.Add(new() { Uid = line.LineId, InboundDocumentId = group.GroupId, LineNumber = inLines.Count + 1,
                        ProductId = line.ProductId, SkuSnapshot = product.Sku, DescriptionSnapshot = description, UnitSnapshot = line.Unit,
                        Quantity = line.Quantity, SourceLineId = line.PurchaseLineId ?? Guid.Empty });
                    outLines.Add(new() { Uid = line.LineId, OutboundDocumentId = group.GroupId, LineNumber = outLines.Count + 1,
                        ProductId = line.ProductId, SkuSnapshot = product.Sku, DescriptionSnapshot = description, UnitSnapshot = line.Unit,
                        Quantity = line.Quantity, CommandItemId = line.LineId });
                    items.Add(new() { CommandItemId = line.LineId, WarehouseSiteId = group.WarehouseSiteId, ProductId = line.ProductId,
                        SkuSnapshot = product.Sku, ProductNameSnapshot = description, UnitSnapshot = line.Unit,
                        QuantityDelta = isInbound ? line.Quantity : -line.Quantity,
                        TransactionType = isInbound ? InventoryTransactionTypeEnum.PackingListAutoReceiptInbound
                            : group.OutboundType == OutboundDocumentTypeEnum.FinalCustomerShipment
                                ? InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound : InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound,
                        SourceDocumentType = isInbound ? "InboundDocument" : "OutboundDocument", SourceDocumentId = group.GroupId,
                        SourceLineId = line.LineId, SourceDocumentNoSnapshot = documentNo, OccurredDate = request.BusinessDate });
                }
                var ih = new InboundDocument { Uid = group.GroupId, DocumentNo = documentNo, DocumentType = InboundDocumentTypeEnum.PurchaseReceipt,
                    CreationMode = InboundCreationModeEnum.Automatic, WarehouseSiteId = group.WarehouseSiteId,
                    WarehouseCodeSnapshot = warehouse!.Code, WarehouseNameSnapshot = warehouse.Name, ReceiptDate = request.BusinessDate,
                    TriggerPackingListId = request.PackingListId, CommandId = group.GroupId, Remark = request.PackingListNo };
                var oh = new OutboundDocument { Uid = group.GroupId, DocumentNo = documentNo, DocumentType = group.OutboundType ?? default,
                    WarehouseSiteId = group.WarehouseSiteId, WarehouseCodeSnapshot = warehouse.Code, WarehouseNameSnapshot = warehouse.Name,
                    ShipmentDate = request.BusinessDate, TriggerPackingListId = request.PackingListId, PackingListNoSnapshot = request.PackingListNo,
                    CommandId = group.GroupId, IssueReason = "PackingList 自动出库" };
                if (isInbound) await repository.InsertInboundAsync(ih, inLines, token);
                else await repository.InsertOutboundAsync(oh, outLines, token);
                var posted = await inventory.PostAsync(request.CommandId, items, token);
                if (posted.Count != items.Count || posted.Select(x => x.CommandItemId).Distinct().Count() != items.Count)
                    Fail("OUTCOME_INVALID", "库存内核未返回完整明细结果。");
                if (isInbound)
                {
                    ih.PostedDate = DateTime.Now; ih.PostedBy = user.UserName;
                    await repository.SetInboundStateAsync(ih, false, token);
                }
                else
                {
                    oh.PostedDate = DateTime.Now; oh.PostedBy = user.UserName;
                    await repository.SetOutboundStateAsync(oh, false, token);
                }
                result.Documents.Add(new() { DocumentId = group.GroupId, DocumentNo = documentNo, Version = isInbound ? ih.Version : oh.Version,
                    PostingStatus = 1, Lines = group.Lines.Select(l => new AutomaticLineResult(l.LineId, l.LineId,
                        posted.Single(x => x.CommandItemId == l.LineId).LedgerId, l.BasisId)).ToList() });
            }
            await SaveResultAsync(request.CommandId, request.CorrelationId, request.PackingListId, request.PackingListNo, operation, fingerprint, result, null, token);
            return result;
        }
        catch (Exception ex) { scope.Abort(); throw MapFailure(ex); }
    }

    /// <summary>按整批采购行聚合容量，避免同采购行跨仓组被逐行重复收货。</summary>
    private async Task ValidatePurchaseCapacityAsync(AutomaticPostingRequest request, CancellationToken token)
    {
        var all = request.Groups.SelectMany(g => g.Lines.Select(l => (Group: g, Line: l))).ToArray();
        var ids = all.Select(x => x.Line.PurchaseLineId!.Value).Distinct().Order().ToArray();
        if (await sourceRepository.HasUnprovenReceiptAsync(token)) Fail("RECEIPT_HISTORY_INVALID", "存在无法证明来源的正式收货历史。");
        var sources = (await sourceRepository.ReadPostingSourcesAsync(ids, token)).ToDictionary(x => x.SourceLineId);
        foreach (var group in all.GroupBy(x => x.Line.PurchaseLineId!.Value))
        {
            if (!sources.TryGetValue(group.Key, out var s) || !s.IsReceivable || s.IsCustomerSupplied)
                Fail("PURCHASE_NOT_RECEIVABLE", "采购来源不允许成品自动收货，客供件必须手动入库。", group.Key);
            if (s!.PostedNetQty < 0 || s.OriginalQty <= 0 || group.Sum(x => x.Line.Quantity) > s.OriginalQty - s.PostedNetQty)
                Fail("PURCHASE_CAPACITY_EXCEEDED", "本批自动入库超过采购剩余可收数量。", group.Key);
            var supplier = await repository.GetPurchaseSupplierAsync(group.Key, token);
            foreach (var row in group)
            {
                var warehouse = await warehouses.GetByIdAsync(row.Group.WarehouseSiteId, true);
                if (!supplier.HasValue || warehouse?.SupplierId != supplier || warehouse.IsDeleted || !warehouse.IsActive
                    || warehouse.SiteType != WarehouseSiteTypeEnum.SupplierVirtual)
                    Fail("SUPPLIER_WAREHOUSE_INVALID", "自动入库仓必须是采购供应商的有效虚拟仓。", row.Line.LineId);
                if (s.ProductId != row.Line.ProductId || !SameUnit(s.Unit, row.Line.Unit) || !SameUnit(s.CurrentUnit, row.Line.Unit))
                    Fail("PRODUCT_UNIT_INVALID", "采购产品或计量单位不一致。", row.Line.LineId);
            }
        }
    }

    /// <summary>固定业务输入约束；正数量不接受隐式舍入，产品相同不代表业务依据相同。</summary>
    internal static void Validate(AutomaticPostingRequest request, bool isInbound)
    {
        request.PackingListNo = request.PackingListNo?.Trim() ?? "";
        if (request.BusinessDate == default || request.PackingListNo.Length is < 1 or > 100 || request.Groups.Count is < 1 or > 500)
            Fail("VALIDATION", "箱单编号、业务日期或单据组数量无效。");
        var ids = new HashSet<Guid>();
        var grouping = new HashSet<(Guid, OutboundDocumentTypeEnum?)>();
        foreach (var group in request.Groups)
        {
            if (group.GroupId == Guid.Empty || !ids.Add(group.GroupId) || group.WarehouseSiteId == Guid.Empty || group.Lines.Count is < 1 or > 500
                || !grouping.Add((group.WarehouseSiteId, group.OutboundType))) Fail("VALIDATION", "组身份、仓库、类型或明细数量无效。");
            if (isInbound ? group.OutboundType.HasValue : group.OutboundType is not (OutboundDocumentTypeEnum.FinalCustomerShipment or OutboundDocumentTypeEnum.CustomerSuppliedConsumption))
                Fail("UNSUPPORTED_TYPE", "自动单据类型不受支持。");
            foreach (var line in group.Lines)
            {
                line.Unit = line.Unit?.Trim().ToUpperInvariant() ?? "";
                if (line.LineId == Guid.Empty || !ids.Add(line.LineId) || line.BasisId == Guid.Empty || line.ProductId == Guid.Empty
                    || line.Unit.Length is < 1 or > 100 || line.Quantity <= 0 || line.Quantity > 99999999999999.9999m
                    || decimal.Round(line.Quantity, 4) != line.Quantity
                    || (isInbound ? line.PurchaseLineId is null || line.PurchaseLineId == Guid.Empty : line.PurchaseLineId.HasValue))
                    Fail("VALIDATION", "明细身份、依据、数量、单位或采购字段无效。", line.LineId);
                // 去掉十进制尾零，仅统一指纹表示，不改变或舍入业务数量。
                line.Quantity = decimal.Parse(line.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture);
                // 去掉十进制尾零，仅统一指纹表示，不改变或舍入业务数量。
                line.Quantity = decimal.Parse(line.Quantity.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture),
                    System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }

    private void RequireUser()
    {
        if (!user.IsAuthenticated || user.UserId is null || user.UserId == Guid.Empty) Fail("UNAUTHENTICATED", "自动库存操作必须具有当前用户身份。");
    }

    /// <summary>日志是批次幂等结果权威，库存明细幂等为第二道保护。</summary>
    private async Task<AutomaticPostingResult?> ReplayAsync(Guid command, string operation, string fingerprint)
    {
        var prior = await logs.GetByModuleCommandAsync(Module, command);
        if (prior is null) return null;
        if (prior.OperationType != operation || prior.UserUid != user.UserId || prior.ChangedFieldsJson != fingerprint)
            Fail("COMMAND_CONFLICT", "命令已用于不同用户、操作或内容。");
        var result = JsonSerializer.Deserialize<AutomaticPostingResult>(prior.RelatedObjectsJson ?? "null");
        if (result is null || result.Documents.Count == 0 || result.Documents.Any(x => x.DocumentId == Guid.Empty || x.Lines.Count == 0))
            Fail("OUTCOME_INVALID", "历史自动单据结果不完整，不能重放。");
        result!.AlreadyApplied = true;
        return result;
    }

    private async Task SaveResultAsync(Guid command, Guid correlation, Guid packingList, string? no, string operation,
        string fingerprint, AutomaticPostingResult result, string? reason, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            CommandId = command, CorrelationId = correlation.ToString("N"), Module = Module, OperationType = operation,
            OperationName = "箱单自动库存操作", AggregateType = "PackingListHead", AggregateUid = packingList,
            AggregateNoSnapshot = no, Summary = "自动单据、库存和结果加入外层事务。", Reason = reason,
            ChangedFieldsJson = fingerprint, RelatedObjectsJson = JsonSerializer.Serialize(result), UserUid = user.UserId,
            UserNameSnapshot = user.UserName, OccurredDate = DateTime.Now
        }) != 1) Fail("LOG_WRITE_FAILED", "自动单据结果日志写入失败。");
        token.ThrowIfCancellationRequested();
    }

    private static string Fingerprint<T>(T request) => JsonSerializer.Serialize(new
    { Fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(request)))) });
    private static bool SameUnit(string? left, string? right) => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right)
        && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    /// <summary>为内部调用方保留业务、取消和并发类别，未预期写入错误同样禁止继续提交。</summary>
    private static Exception MapFailure(Exception error) => error switch
    {
        AutomaticPostingException or OperationCanceledException => error,
        System.Data.DBConcurrencyException => new AutomaticPostingException("VERSION_CONFLICT", error.Message),
        InventoryPostingException => new AutomaticPostingException(error.Message.StartsWith("COMMAND_CONFLICT", StringComparison.Ordinal)
            ? "COMMAND_CONFLICT" : "INVENTORY_BLOCKED", error.Message),
        _ => new AutomaticPostingException("WRITE_FAILED", "自动库存操作失败，当前事务已回滚：" + error.Message)
    };
    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    private static void Fail(string code, string message, Guid? id = null) => AutomaticPostingScope.Fail(code, message, id);
}

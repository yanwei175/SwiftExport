using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>在调用方事务内维护不可变库存流水及其余额投影。</summary>
public sealed class InventoryPostingService(ServiceBaseContext context, IInventoryRepository inventory, IWarehouseSiteRepository warehouseSites) : IInventoryPostingService
{
    private readonly DbSession _session = context.Session;

    /// <summary>追加普通库存事实；同一 CommandId 可分批追加不同 CommandItemId。</summary>
    public Task<IReadOnlyList<InventoryPostingResultDto>> PostAsync(Guid commandId, IReadOnlyList<InventoryPostingItem> items, CancellationToken cancellationToken = default) =>
        ProtectAsync(async () =>
        {
            ValidateCommand(commandId, items?.Count ?? 0);
            var submitted = items!;
            var normal = submitted.Where(x => x.TransactionType != InventoryTransactionTypeEnum.Reversal).ToList();
            var legacyReversals = submitted.Where(x => x.TransactionType == InventoryTransactionTypeEnum.Reversal).ToList();
            if (normal.Count > 0 && legacyReversals.Count > 0)
                throw new InventoryPostingException("普通流水与反向流水必须分开提交。");
            if (legacyReversals.Count > 0)
                return await ReverseLegacyAsync(commandId, legacyReversals, cancellationToken);
            ValidateNormalItems(normal);
            await AcquireCommandLockAsync(commandId, cancellationToken);
            var facts = normal.Select(x => PostingFact.From(commandId, x)).ToList();
            return await ApplyAsync(facts, false, cancellationToken);
        });

    /// <summary>按锁定原流水生成完整反向事实，调用方不能覆盖库存身份或数量。</summary>
    public Task<IReadOnlyList<InventoryPostingResultDto>> ReverseAsync(Guid commandId, IReadOnlyList<InventoryReverseItem> items, CancellationToken cancellationToken = default) =>
        ProtectAsync(async () =>
        {
            ValidateCommand(commandId, items?.Count ?? 0);
            ValidateReverseItems(items!);
            await AcquireCommandLockAsync(commandId, cancellationToken);
            var facts = await BuildReverseFactsAsync(commandId, items!, null, cancellationToken);
            return await ApplyAsync(facts, false, cancellationToken);
        });

    /// <summary>原子追加完整反向与替代事实，只按每个余额键的整组最终数量判断负库存。</summary>
    public Task<InventoryCorrectionResult> CorrectAsync(Guid commandId, IReadOnlyList<InventoryCorrectionItem> items, CancellationToken cancellationToken = default) =>
        ProtectAsync(async () =>
        {
            ValidateCommand(commandId, items?.Count ?? 0);
            var submitted = items!;
            ValidateCorrections(submitted);
            await AcquireCommandLockAsync(commandId, cancellationToken);
            var existingGroup = await inventory.LockCorrectionGroupAsync(commandId);
            if (existingGroup.Count > 0)
                return ReplayCorrection(commandId, items!, existingGroup);

            var reverseRequests = submitted.Select(x => new InventoryReverseItem
            {
                CommandItemId = x.ReverseCommandItemId,
                OriginalLedgerId = x.OriginalLedgerId,
                SourceDocumentType = x.ReverseSourceDocumentType,
                SourceDocumentId = x.ReverseSourceDocumentId,
                SourceLineId = x.ReverseSourceLineId,
                SourceDocumentNoSnapshot = x.ReverseSourceDocumentNoSnapshot,
                Reason = x.Reason,
                OccurredDate = x.ReverseOccurredDate
            }).ToList();
            var reversals = await BuildReverseFactsAsync(commandId, reverseRequests, commandId, cancellationToken);
            var originals = reversals.ToDictionary(x => x.ReversalOfLedgerId!.Value);
            var replacements = new List<PostingFact>();
            foreach (var correction in submitted.OrderBy(x => x.OriginalLedgerId))
            {
                var original = originals[correction.OriginalLedgerId].Original!;
                var priorReplacement = await inventory.LockReplacementByOriginalAsync(original.Uid);
                if (priorReplacement is not null)
                    throw new InventoryPostingException("原库存流水已经存在纠错替代事实。");
                var replacement = correction.Replacement;
                ValidateNormalItem(replacement);
                if (replacement.WarehouseSiteId != original.WarehouseSiteId || replacement.ProductId != original.ProductId)
                    throw new InventoryPostingException("纠错替代事实必须与原流水使用相同仓库和产品。");
                if (!string.IsNullOrWhiteSpace(replacement.UnitSnapshot) && !Same(replacement.UnitSnapshot, original.UnitSnapshot))
                    throw new InventoryPostingException("纠错替代事实必须与原流水使用相同单位。");
                var fact = PostingFact.FromCorrection(commandId, replacement, original.Uid);
                fact.UseOriginalSnapshots(original);
                replacements.Add(fact);
            }

            // 组内按余额键、原流水、反向在前替代在后形成确定顺序，但只校验最终余额。
            var combined = reversals.Concat(replacements)
                .OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.ProductId)
                .ThenBy(x => x.ReversalOfLedgerId ?? x.ReplacementOfLedgerId)
                .ThenBy(x => x.ReplacementOfLedgerId.HasValue)
                .ToList();
            var results = await ApplyAsync(combined, true, cancellationToken);
            var byCommandItem = results.ToDictionary(x => x.CommandItemId);
            return new InventoryCorrectionResult
            {
                Reversals = reversals.Select(x => byCommandItem[x.CommandItemId]).ToList(),
                Replacements = replacements.Select(x => byCommandItem[x.CommandItemId]).ToList()
            };
        });

    private async Task<IReadOnlyList<InventoryPostingResultDto>> ReverseLegacyAsync(Guid commandId, IReadOnlyList<InventoryPostingItem> items, CancellationToken cancellationToken)
    {
        if (items.Any(x => !x.ReversalOfLedgerId.HasValue))
            throw new InventoryPostingException("反向流水必须引用原流水。");
        var requests = items.Select(x => new InventoryReverseItem
        {
            CommandItemId = x.CommandItemId,
            OriginalLedgerId = x.ReversalOfLedgerId!.Value,
            SourceDocumentType = x.SourceDocumentType,
            SourceDocumentId = x.SourceDocumentId,
            SourceLineId = x.SourceLineId,
            SourceDocumentNoSnapshot = x.SourceDocumentNoSnapshot,
            Reason = x.Reason ?? string.Empty,
            OccurredDate = x.OccurredDate
        }).ToList();
        ValidateReverseItems(requests);
        await AcquireCommandLockAsync(commandId, cancellationToken);
        var facts = await BuildReverseFactsAsync(commandId, requests, null, cancellationToken);
        var originals = facts.ToDictionary(x => x.CommandItemId, x => x.Original!);
        foreach (var expected in items)
        {
            var original = originals[expected.CommandItemId];
            if (expected.WarehouseSiteId != original.WarehouseSiteId || expected.ProductId != original.ProductId || expected.QuantityDelta != -original.QuantityDelta || !Same(expected.UnitSnapshot, original.UnitSnapshot))
                throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：反向请求与原流水库存事实不一致。");
        }
        return await ApplyAsync(facts, false, cancellationToken);
    }

    private async Task<List<PostingFact>> BuildReverseFactsAsync(Guid commandId, IReadOnlyList<InventoryReverseItem> items, Guid? correctionGroupId, CancellationToken cancellationToken)
    {
        var facts = new List<PostingFact>();
        foreach (var item in items.OrderBy(x => x.OriginalLedgerId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var original = await inventory.LockLedgerByIdAsync(item.OriginalLedgerId) ?? throw new InventoryPostingException("被反向的原库存流水不存在。");
            if (original.TransactionType == InventoryTransactionTypeEnum.Reversal || original.ReversalOfLedgerId.HasValue)
                throw new InventoryPostingException("反向流水不能再次反向。");
            var prior = await inventory.LockReversalByOriginalAsync(original.Uid);
            if (prior is not null && prior.CommandItemId != item.CommandItemId)
                throw new InventoryPostingException("库存流水已经被反向。");
            facts.Add(PostingFact.Reverse(commandId, item, original, correctionGroupId));
        }
        return facts;
    }

    private async Task<IReadOnlyList<InventoryPostingResultDto>> ApplyAsync(IReadOnlyList<PostingFact> facts, bool validateFinalOnly, CancellationToken cancellationToken)
    {
        var existing = new Dictionary<Guid, InventoryLedgerEntry>();
        foreach (var fact in facts.OrderBy(x => x.CommandItemId))
        {
            var ledger = await inventory.LockLedgerByCommandItemAsync(fact.CommandItemId);
            if (ledger is null) continue;
            if (!SameFact(ledger, fact)) throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：CommandItemId 已用于不同库存事实。");
            existing.Add(fact.CommandItemId, ledger);
        }

        var pending = facts.Where(x => !existing.ContainsKey(x.CommandItemId)).ToList();
        var sites = new Dictionary<Guid, WarehouseSite>();
        var products = new Dictionary<Guid, Product>();
        foreach (var warehouseId in pending.Select(x => x.WarehouseSiteId).Distinct().OrderBy(x => x))
        {
            // 与软删除共用仓库行锁；反向必须先恢复原仓，不能悄悄改到其他仓库。
            var site = await warehouseSites.GetForUpdateAsync(warehouseId);
            if (site is null || site.IsDeleted || !site.IsActive)
                throw new InventoryPostingException(pending.Any(x => x.WarehouseSiteId == warehouseId && x.TransactionType == InventoryTransactionTypeEnum.Reversal)
                    ? "原实际仓库已删除、停用或不存在，请先恢复原仓库后再反过账。"
                    : "仓库已删除、停用或不存在，请重新选择有效仓库。");
            sites.Add(warehouseId, site);
        }
        foreach (var productId in pending.Select(x => x.ProductId).Distinct().OrderBy(x => x))
        {
            var requiresCurrentMaster = pending.Any(x => x.ProductId == productId && x.TransactionType != InventoryTransactionTypeEnum.Reversal && !x.CorrectionGroupId.HasValue);
            var product = await inventory.GetProductForPostingAsync(productId);
            if (product is null && requiresCurrentMaster) throw new InventoryPostingException("产品不存在或已删除。");
            if (requiresCurrentMaster && string.IsNullOrWhiteSpace(product?.Unit))
                throw new InventoryPostingException("产品基础单位为空，不能建立新的库存事实。");
            if (product is null)
            {
                // 完整反向和纠错使用原流水快照，不能因历史主数据停用而失去纠错能力。
                var snapshot = pending.First(x => x.ProductId == productId);
                product = new Product { Uid = productId, Sku = snapshot.SkuSnapshot ?? string.Empty, DescriptionZh = snapshot.ProductNameSnapshot, Unit = snapshot.UnitSnapshot };
            }
            foreach (var fact in pending.Where(x => x.ProductId == productId && x.TransactionType != InventoryTransactionTypeEnum.Reversal && !x.CorrectionGroupId.HasValue))
            {
                if (!string.IsNullOrWhiteSpace(fact.UnitSnapshot) && !string.IsNullOrWhiteSpace(product.Unit) && !Same(fact.UnitSnapshot, product.Unit))
                    throw new InventoryPostingException("过账单位必须与产品基础单位一致。");
            }
            products.Add(productId, product);
        }

        var balances = new Dictionary<(Guid, Guid), InventoryBalance>();
        foreach (var key in pending.Select(x => (x.WarehouseSiteId, x.ProductId)).Distinct().OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.ProductId))
        {
            var balance = await inventory.LockBalanceAsync(key.WarehouseSiteId, key.ProductId);
            if (balance is null)
            {
                var first = pending.First(x => x.WarehouseSiteId == key.WarehouseSiteId && x.ProductId == key.ProductId);
                var product = products[key.ProductId];
                if (GroupIsHistorical(pending, key))
                    throw new InventoryPostingException("原库存流水缺少余额投影，不能反向或纠错；请先完成数据治理。");
                balance = new InventoryBalance
                {
                    Uid = GuidSequentialGenerator.NewGuid(), WarehouseSiteId = key.WarehouseSiteId, ProductId = key.ProductId,
                    SkuSnapshot = Snapshot(first.SkuSnapshot, product.Sku, string.Empty),
                    ProductNameSnapshot = Snapshot(first.ProductNameSnapshot, product.DescriptionZh, product.DescriptionEn ?? product.Sku ?? string.Empty),
                    UnitSnapshot = Snapshot(product.Unit, first.UnitSnapshot, "PCS")
                };
                if (await inventory.InsertBalanceAsync(balance) != 1) throw new InventoryPostingException("库存余额创建失败。");
            }
            else if (balance.IsDeleted)
                throw new InventoryPostingException("库存余额已被删除，禁止继续过账；请先完成遗留数据治理。");
            balances.Add(key, balance);
        }

        var newResults = new Dictionary<Guid, InventoryPostingResultDto>();
        foreach (var group in pending.GroupBy(x => (x.WarehouseSiteId, x.ProductId)).OrderBy(x => x.Key.WarehouseSiteId).ThenBy(x => x.Key.ProductId))
        {
            var balance = balances[group.Key];
            var expectedVersion = balance.Version;
            foreach (var fact in group)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var site = sites[fact.WarehouseSiteId];
                var product = products[fact.ProductId];
                var unit = Snapshot(fact.UnitSnapshot, product.Unit, "PCS");
                if (!string.IsNullOrWhiteSpace(balance.UnitSnapshot) && !Same(balance.UnitSnapshot, unit))
                    throw new InventoryPostingException("库存余额单位与过账单位不一致。");
                var before = balance.OnHandQty;
                var after = before + fact.QuantityDelta;
                if (!validateFinalOnly && after < 0) throw new InventoryPostingException("库存不足，过账后库存不能为负。");
                var sequence = await inventory.NextLedgerSequenceAsync();
                balance.OnHandQty = after;
                balance.LastLedgerSequence = sequence;
                balance.SkuSnapshot = Snapshot(fact.SkuSnapshot, product.Sku, balance.SkuSnapshot ?? string.Empty);
                balance.ProductNameSnapshot = Snapshot(fact.ProductNameSnapshot, product.DescriptionZh, product.DescriptionEn ?? balance.ProductNameSnapshot ?? string.Empty);
                balance.UnitSnapshot = unit;
                var ledger = fact.ToEntity(sequence, after, site, balance);
                if (await inventory.InsertLedgerAsync(ledger) != 1) throw new InventoryPostingException("库存流水写入失败。");
                newResults.Add(fact.CommandItemId, Result(ledger, before, false));
            }
            if (balance.OnHandQty < 0) throw new InventoryPostingException("纠错后最终库存不能为负。");
            if (await inventory.UpdateBalanceAsync(balance, expectedVersion) != 1) throw new InventoryPostingException("库存余额发生并发冲突，请重试。");
        }

        return facts.Select(x => existing.TryGetValue(x.CommandItemId, out var ledger) ? Result(ledger, ledger.BalanceAfter - ledger.QuantityDelta, true) : newResults[x.CommandItemId]).ToList();
    }

    private async Task AcquireCommandLockAsync(Guid commandId, CancellationToken cancellationToken)
    {
        if (await inventory.AcquireCommandLockAsync(commandId, cancellationToken) < 0)
            throw new InventoryPostingException("库存命令正在处理中，请重试。");
    }

    private async Task<T> ProtectAsync<T>(Func<Task<T>> action)
    {
        if (_session.Transaction is null) throw new InventoryPostingException("库存过账必须由业务服务在已有事务内调用。");
        try { return await action(); }
        catch
        {
            // 只回滚但不释放、不清空外层事务，确保调用方不能误提交部分业务状态。
            try { _session.Transaction?.Rollback(); } catch { }
            throw;
        }
    }

    private static void ValidateCommand(Guid commandId, int count)
    {
        if (commandId == Guid.Empty || count == 0) throw new InventoryPostingException("库存过账命令或明细不能为空。");
    }

    private static void ValidateNormalItems(IReadOnlyList<InventoryPostingItem> items)
    {
        if (items.Select(x => x.CommandItemId).Distinct().Count() != items.Count) throw new InventoryPostingException("同一请求中的 CommandItemId 必须唯一。");
        foreach (var item in items) ValidateNormalItem(item);
    }

    private static void ValidateNormalItem(InventoryPostingItem item)
    {
        if (item.CommandItemId == Guid.Empty || item.WarehouseSiteId == Guid.Empty || item.ProductId == Guid.Empty || item.QuantityDelta == 0 || item.OccurredDate == default)
            throw new InventoryPostingException("库存过账明细不完整。");
        if (!item.SourceDocumentId.HasValue || item.SourceDocumentId == Guid.Empty || !item.SourceLineId.HasValue || item.SourceLineId == Guid.Empty)
            throw new InventoryPostingException("库存过账必须包含可追溯的来源单据与来源行标识。");
        ValidateQuantity(item.QuantityDelta);
        if (item.ReversalOfLedgerId.HasValue || item.ReplacementOfLedgerId.HasValue)
            throw new InventoryPostingException("普通过账不能设置反向或替代关系。");
        var inbound = item.TransactionType is InventoryTransactionTypeEnum.PurchaseReceiptInbound or InventoryTransactionTypeEnum.CustomerSuppliedReceiptInbound or InventoryTransactionTypeEnum.PackingListAutoReceiptInbound;
        // 普通库存出库共用负向交易校验，不扩大负库存例外入口。
        var outbound = item.TransactionType is InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound or InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound or InventoryTransactionTypeEnum.StockIssueOutbound;
        if ((!inbound && !outbound) || (inbound && item.QuantityDelta < 0) || (outbound && item.QuantityDelta > 0))
            throw new InventoryPostingException("库存交易类型与数量方向不一致，且不允许普通接口写入期初、负库存异常或反向类型。");
        ValidateText(item.SourceDocumentType, 50, true, "来源单据类型");
        ValidateText(item.SourceDocumentNoSnapshot, 100, false, "来源单号");
        ValidateText(item.Reason, 500, false, "原因");
        ValidateText(item.SkuSnapshot, 200, false, "SKU 快照");
        ValidateText(item.ProductNameSnapshot, 500, false, "产品名称快照");
        ValidateText(item.UnitSnapshot, 100, false, "单位快照");
    }

    private static void ValidateReverseItems(IReadOnlyList<InventoryReverseItem> items)
    {
        if (items.Select(x => x.CommandItemId).Distinct().Count() != items.Count || items.Select(x => x.OriginalLedgerId).Distinct().Count() != items.Count)
            throw new InventoryPostingException("反向请求中的 CommandItemId 和原流水必须唯一。");
        foreach (var item in items)
        {
            if (item.CommandItemId == Guid.Empty || item.OriginalLedgerId == Guid.Empty || item.OccurredDate == default || string.IsNullOrWhiteSpace(item.Reason))
                throw new InventoryPostingException("反向请求必须包含幂等键、原流水、发生时间和原因。");
            if (!item.SourceDocumentId.HasValue || item.SourceDocumentId == Guid.Empty || !item.SourceLineId.HasValue || item.SourceLineId == Guid.Empty)
                throw new InventoryPostingException("反向请求必须包含可追溯的来源单据与来源行标识。");
            ValidateText(item.SourceDocumentType, 50, true, "来源单据类型");
            ValidateText(item.SourceDocumentNoSnapshot, 100, false, "来源单号");
            ValidateText(item.Reason, 500, true, "原因");
        }
    }

    private static void ValidateCorrections(IReadOnlyList<InventoryCorrectionItem> items)
    {
        if (items.Select(x => x.OriginalLedgerId).Distinct().Count() != items.Count || items.Select(x => x.ReverseCommandItemId).Distinct().Count() != items.Count || items.Select(x => x.Replacement.CommandItemId).Distinct().Count() != items.Count)
            throw new InventoryPostingException("纠错请求中的原流水、反向幂等键和替代幂等键必须唯一。");
        if (items.SelectMany(x => new[] { x.ReverseCommandItemId, x.Replacement.CommandItemId }).Distinct().Count() != items.Count * 2)
            throw new InventoryPostingException("纠错组内所有 CommandItemId 必须唯一。");
        ValidateReverseItems(items.Select(x => new InventoryReverseItem { CommandItemId = x.ReverseCommandItemId, OriginalLedgerId = x.OriginalLedgerId, SourceDocumentType = x.ReverseSourceDocumentType, SourceDocumentId = x.ReverseSourceDocumentId, SourceLineId = x.ReverseSourceLineId, SourceDocumentNoSnapshot = x.ReverseSourceDocumentNoSnapshot, Reason = x.Reason, OccurredDate = x.ReverseOccurredDate }).ToList());
        foreach (var item in items) ValidateNormalItem(item.Replacement);
    }

    private static InventoryCorrectionResult ReplayCorrection(Guid commandId, IReadOnlyList<InventoryCorrectionItem> requested, IReadOnlyList<InventoryLedgerEntry> group)
    {
        if (group.Count != requested.Count * 2 || group.Any(x => x.CommandId != commandId)) throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：纠错命令的事实集合已冻结且与本次不同。");
        var reversals = new List<InventoryPostingResultDto>();
        var replacements = new List<InventoryPostingResultDto>();
        foreach (var item in requested)
        {
            var reverse = group.SingleOrDefault(x => x.CommandItemId == item.ReverseCommandItemId && x.ReversalOfLedgerId == item.OriginalLedgerId);
            var replacement = group.SingleOrDefault(x => x.CommandItemId == item.Replacement.CommandItemId && x.ReplacementOfLedgerId == item.OriginalLedgerId);
            if (reverse is null || replacement is null || !SameReverseRequest(reverse, item) || !SameFact(replacement, PostingFact.FromCorrection(commandId, item.Replacement, item.OriginalLedgerId)))
                throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：纠错命令的事实集合已冻结且与本次不同。");
            reversals.Add(Result(reverse, reverse.BalanceAfter - reverse.QuantityDelta, true));
            replacements.Add(Result(replacement, replacement.BalanceAfter - replacement.QuantityDelta, true));
        }
        return new InventoryCorrectionResult { Reversals = reversals, Replacements = replacements };
    }

    private static bool SameReverseRequest(InventoryLedgerEntry ledger, InventoryCorrectionItem item) =>
        ledger.CorrectionGroupId == ledger.CommandId && ledger.OccurredDate == item.ReverseOccurredDate && Same(ledger.SourceDocumentType, item.ReverseSourceDocumentType)
        && ledger.SourceDocumentId == item.ReverseSourceDocumentId && ledger.SourceLineId == item.ReverseSourceLineId
        && Same(ledger.SourceDocumentNoSnapshot, item.ReverseSourceDocumentNoSnapshot) && Same(ledger.Reason, item.Reason);

    private static bool SameFact(InventoryLedgerEntry ledger, PostingFact fact) => ledger.CommandId == fact.CommandId && ledger.CommandItemId == fact.CommandItemId
        && (string.IsNullOrWhiteSpace(ledger.RequestFingerprint)
            ? SameLegacyFact(ledger, fact)
            : string.Equals(ledger.RequestFingerprint, fact.RequestFingerprint, StringComparison.Ordinal));

    /// <summary>仅供迁移前无请求指纹的历史流水使用严格比较，不再把缺省快照当通配符。</summary>
    private static bool SameLegacyFact(InventoryLedgerEntry ledger, PostingFact fact) => ledger.CommandId == fact.CommandId && ledger.CommandItemId == fact.CommandItemId
        && ledger.WarehouseSiteId == fact.WarehouseSiteId && ledger.ProductId == fact.ProductId && ledger.TransactionType == fact.TransactionType
        && ledger.QuantityDelta == fact.QuantityDelta && ledger.OccurredDate == fact.OccurredDate && ledger.SourceDocumentId == fact.SourceDocumentId
        && ledger.SourceLineId == fact.SourceLineId && ledger.ReversalOfLedgerId == fact.ReversalOfLedgerId && ledger.ReplacementOfLedgerId == fact.ReplacementOfLedgerId
        && ledger.CorrectionGroupId == fact.CorrectionGroupId && Same(ledger.SourceDocumentType, fact.SourceDocumentType)
        && Same(ledger.SourceDocumentNoSnapshot, fact.SourceDocumentNoSnapshot) && Same(ledger.Reason, fact.Reason)
        && Same(ledger.SkuSnapshot, fact.SkuSnapshot) && Same(ledger.ProductNameSnapshot, fact.ProductNameSnapshot)
        && Same(ledger.UnitSnapshot, fact.UnitSnapshot);

    private static InventoryPostingResultDto Result(InventoryLedgerEntry ledger, decimal before, bool applied) => new() { CommandItemId = ledger.CommandItemId, LedgerId = ledger.Uid, BeforeQty = before, Delta = ledger.QuantityDelta, AfterQty = ledger.BalanceAfter, AlreadyApplied = applied };
    private static bool Same(string? left, string? right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
    private static string Snapshot(string? preferred, string? fallback, string defaultValue) => string.IsNullOrWhiteSpace(preferred) ? (string.IsNullOrWhiteSpace(fallback) ? defaultValue : fallback.Trim()) : preferred.Trim();
    private static void ValidateText(string? value, int max, bool required, string name) { var length = value?.Trim().Length ?? 0; if ((required && length == 0) || length > max) throw new InventoryPostingException($"{name}不能为空且长度不能超过 {max}。" ); }
    private static void ValidateQuantity(decimal quantity) { if (decimal.Round(quantity, 4) != quantity || Math.Abs(quantity) > 99999999999999.9999m) throw new InventoryPostingException("库存数量最多保留 4 位小数且不能超出 decimal(18,4) 范围。"); }
    /// <summary>判断余额键是否只包含从历史原流水派生的反向或纠错事实。</summary>
    private static bool GroupIsHistorical(IReadOnlyList<PostingFact> pending, (Guid WarehouseSiteId, Guid ProductId) key) => pending.Where(x => x.WarehouseSiteId == key.WarehouseSiteId && x.ProductId == key.ProductId).All(x => x.TransactionType == InventoryTransactionTypeEnum.Reversal || x.CorrectionGroupId.HasValue);

    private sealed class PostingFact
    {
        public Guid CommandId { get; init; }
        public Guid CommandItemId { get; init; }
        public Guid WarehouseSiteId { get; init; }
        public Guid ProductId { get; init; }
        public decimal QuantityDelta { get; init; }
        public InventoryTransactionTypeEnum TransactionType { get; init; }
        public string SourceDocumentType { get; init; } = string.Empty;
        public Guid? SourceDocumentId { get; init; }
        public Guid? SourceLineId { get; init; }
        public string? SourceDocumentNoSnapshot { get; init; }
        public Guid? ReversalOfLedgerId { get; init; }
        public Guid? ReplacementOfLedgerId { get; set; }
        public Guid? CorrectionGroupId { get; set; }
        public string RequestFingerprint { get; private set; } = string.Empty;
        public string? Reason { get; init; }
        public DateTime OccurredDate { get; init; }
        public string? SkuSnapshot { get; private set; }
        public string? ProductNameSnapshot { get; private set; }
        public string? UnitSnapshot { get; private set; }
        public InventoryLedgerEntry? Original { get; init; }

        public static PostingFact From(Guid commandId, InventoryPostingItem item) { var fact = new PostingFact { CommandId = commandId, CommandItemId = item.CommandItemId, WarehouseSiteId = item.WarehouseSiteId, ProductId = item.ProductId, QuantityDelta = item.QuantityDelta, TransactionType = item.TransactionType, SourceDocumentType = item.SourceDocumentType.Trim(), SourceDocumentId = item.SourceDocumentId, SourceLineId = item.SourceLineId, SourceDocumentNoSnapshot = Normalize(item.SourceDocumentNoSnapshot), Reason = Normalize(item.Reason), OccurredDate = item.OccurredDate, SkuSnapshot = Normalize(item.SkuSnapshot), ProductNameSnapshot = Normalize(item.ProductNameSnapshot), UnitSnapshot = Normalize(item.UnitSnapshot) }; fact.RefreshFingerprint(); return fact; }
        public static PostingFact FromCorrection(Guid commandId, InventoryPostingItem item, Guid originalId) { var fact = From(commandId, item); fact.ReplacementOfLedgerId = originalId; fact.CorrectionGroupId = commandId; fact.RefreshFingerprint(); return fact; }
        public static PostingFact Reverse(Guid commandId, InventoryReverseItem item, InventoryLedgerEntry original, Guid? groupId) { var fact = new PostingFact { CommandId = commandId, CommandItemId = item.CommandItemId, WarehouseSiteId = original.WarehouseSiteId, ProductId = original.ProductId, QuantityDelta = -original.QuantityDelta, TransactionType = InventoryTransactionTypeEnum.Reversal, SourceDocumentType = item.SourceDocumentType.Trim(), SourceDocumentId = item.SourceDocumentId, SourceLineId = item.SourceLineId, SourceDocumentNoSnapshot = Normalize(item.SourceDocumentNoSnapshot), ReversalOfLedgerId = original.Uid, CorrectionGroupId = groupId, Reason = Normalize(item.Reason), OccurredDate = item.OccurredDate, SkuSnapshot = Normalize(original.SkuSnapshot), ProductNameSnapshot = Normalize(original.ProductNameSnapshot), UnitSnapshot = Normalize(original.UnitSnapshot), Original = original }; fact.RefreshFingerprint(); return fact; }
        public InventoryLedgerEntry ToEntity(long sequence, decimal after, WarehouseSite site, InventoryBalance balance) => new() { Uid = GuidSequentialGenerator.NewGuid(), Sequence = sequence, WarehouseSiteId = WarehouseSiteId, ProductId = ProductId, TransactionType = TransactionType, QuantityDelta = QuantityDelta, BalanceAfter = after, SourceDocumentType = SourceDocumentType, SourceDocumentId = SourceDocumentId, SourceLineId = SourceLineId, SourceDocumentNoSnapshot = SourceDocumentNoSnapshot, ReversalOfLedgerId = ReversalOfLedgerId, ReplacementOfLedgerId = ReplacementOfLedgerId, CorrectionGroupId = CorrectionGroupId, CommandId = CommandId, CommandItemId = CommandItemId, RequestFingerprint = RequestFingerprint, Reason = Reason, OccurredDate = OccurredDate, WarehouseCodeSnapshot = site.Code, WarehouseNameSnapshot = site.Name, SkuSnapshot = balance.SkuSnapshot, ProductNameSnapshot = balance.ProductNameSnapshot, UnitSnapshot = balance.UnitSnapshot };
        /// <summary>纠错替代缺省快照从原事实派生，但已计算的请求指纹继续保留原始缺省语义。</summary>
        public void UseOriginalSnapshots(InventoryLedgerEntry original) { SkuSnapshot = Normalize(SkuSnapshot) ?? Normalize(original.SkuSnapshot); ProductNameSnapshot = Normalize(ProductNameSnapshot) ?? Normalize(original.ProductNameSnapshot); UnitSnapshot = Normalize(UnitSnapshot) ?? Normalize(original.UnitSnapshot); }
        private void RefreshFingerprint() { var canonical = JsonSerializer.Serialize(new object?[] { CommandId, CommandItemId, WarehouseSiteId, ProductId, QuantityDelta, (int)TransactionType, SourceDocumentType, SourceDocumentId, SourceLineId, SourceDocumentNoSnapshot, ReversalOfLedgerId, ReplacementOfLedgerId, CorrectionGroupId, Reason, OccurredDate, SkuSnapshot, ProductNameSnapshot, UnitSnapshot }); RequestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))); }
        private static string? Normalize(string? value) { var text = value?.Trim(); return string.IsNullOrEmpty(text) ? null : text; }
    }
}

/// <summary>表示库存过账因业务规则或幂等冲突而被拒绝。</summary>
public sealed class InventoryPostingException(string message) : Exception(message);

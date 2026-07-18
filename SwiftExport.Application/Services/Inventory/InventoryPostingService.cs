using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Services.Inventory;

public sealed class InventoryPostingService(ServiceBaseContext context, IInventoryRepository inventory, IWarehouseSiteRepository warehouseSites) : IInventoryPostingService
{
    private readonly DbSession _session = context.Session;

    public async Task<IReadOnlyList<InventoryPostingResultDto>> PostAsync(Guid commandId, IReadOnlyList<InventoryPostingItem> items, CancellationToken cancellationToken = default)
    {
        if (commandId == Guid.Empty || items.Count == 0) throw new InventoryPostingException("库存过账命令或明细不能为空。");
        if (_session.Transaction is null) throw new InventoryPostingException("库存过账必须由业务服务在已有事务内调用。");
        if (items.Any(x => x.CommandItemId == Guid.Empty || x.WarehouseSiteId == Guid.Empty || x.ProductId == Guid.Empty || x.QuantityDelta == 0)) throw new InventoryPostingException("库存过账明细不完整。");
        if (items.Select(x => x.CommandItemId).Distinct().Count() != items.Count) throw new InventoryPostingException("同一命令中的 CommandItemId 必须唯一。");

        var orderedItems = items
            .OrderBy(x => x.WarehouseSiteId)
            .ThenBy(x => x.ProductId)
            .ThenBy(x => x.CommandItemId)
            .ToList();
        var preexisting = new Dictionary<Guid, InventoryLedgerEntry>();
        foreach (var item in orderedItems)
        {
            var ledger = await inventory.FindLedgerByCommandItemAsync(item.CommandItemId);
            if (ledger is null) continue;
            if (!IsSamePostingFact(ledger, commandId, item))
                throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：CommandItemId 已用于不同库存事实。");
            preexisting[item.CommandItemId] = ledger;
        }
        var lockedBalances = new Dictionary<(Guid WarehouseSiteId, Guid ProductId), InventoryBalance>();
        var lockedSites = new Dictionary<Guid, WarehouseSite>();

        // 所有调用方使用相同的仓库、产品锁序，避免多仓批量过账互相等待。
        foreach (var key in orderedItems.Where(x => !preexisting.ContainsKey(x.CommandItemId)).Select(x => (x.WarehouseSiteId, x.ProductId)).Distinct())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!lockedSites.TryGetValue(key.WarehouseSiteId, out var site))
            {
                site = await warehouseSites.GetForUpdateAsync(key.WarehouseSiteId)
                    ?? throw new InventoryPostingException("虚拟仓不存在。");
                if (!site.IsActive) throw new InventoryPostingException("虚拟仓已停用，不能过账。");
                lockedSites.Add(key.WarehouseSiteId, site);
            }

            if (!await inventory.IsActiveProductAsync(key.ProductId))
                throw new InventoryPostingException("产品不存在或已停用。");

            var balance = await inventory.LockBalanceAsync(key.WarehouseSiteId, key.ProductId);
            if (balance is null)
            {
                balance = new InventoryBalance
                {
                    Uid = GuidSequentialGenerator.NewGuid(),
                    WarehouseSiteId = key.WarehouseSiteId,
                    ProductId = key.ProductId,
                    OnHandQty = 0,
                    LastLedgerSequence = 0
                };
                if (await inventory.InsertBalanceAsync(balance) != 1)
                    throw new InventoryPostingException("库存余额创建失败。");
            }

            lockedBalances.Add(key, balance);
        }

        var results = new List<InventoryPostingResultDto>();
        foreach (var item in orderedItems)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // 余额键加锁后再次检查，关闭两个并发命令同时越过首次幂等检查的窗口。
            var existing = preexisting.TryGetValue(item.CommandItemId, out var applied)
                ? applied
                : await inventory.LockLedgerByCommandItemAsync(item.CommandItemId);
            if (existing is not null)
            {
                if (!IsSamePostingFact(existing, commandId, item))
                    throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：CommandItemId 已用于不同库存事实。");
                results.Add(new InventoryPostingResultDto { CommandItemId = item.CommandItemId, LedgerId = existing.Uid, Delta = existing.QuantityDelta, AfterQty = existing.BalanceAfter, BeforeQty = existing.BalanceAfter - existing.QuantityDelta, AlreadyApplied = true });
                continue;
            }
            var site = lockedSites[item.WarehouseSiteId];
            var balance = lockedBalances[(item.WarehouseSiteId, item.ProductId)];
            var before = balance.OnHandQty;
            var after = before + item.QuantityDelta;
            if (after < 0 && (!item.AllowNegativeStock || item.TransactionType != InventoryTransactionTypeEnum.NegativeStockExceptionOutbound || string.IsNullOrWhiteSpace(item.Reason)))
                throw new InventoryPostingException("正常出库后库存不能为负；负库存异常必须使用专用类型并填写原因。");
            var sequence = await inventory.NextLedgerSequenceAsync();
            balance.OnHandQty = after; balance.LastLedgerSequence = sequence;
            if (await inventory.UpdateBalanceAsync(balance, balance.Version) != 1) throw new InventoryPostingException("库存余额发生并发冲突，请重试。");
            var ledger = new InventoryLedgerEntry
            {
                Uid = GuidSequentialGenerator.NewGuid(), Sequence = sequence, WarehouseSiteId = item.WarehouseSiteId, ProductId = item.ProductId,
                TransactionType = item.TransactionType, QuantityDelta = item.QuantityDelta, BalanceAfter = after, SourceDocumentType = item.SourceDocumentType,
                SourceDocumentId = item.SourceDocumentId, SourceLineId = item.SourceLineId, ReversalOfLedgerId = item.ReversalOfLedgerId, CommandId = commandId, CommandItemId = item.CommandItemId,
                Reason = Normalize(item.Reason, 500), OccurredDate = item.OccurredDate == default ? DateTime.Now : item.OccurredDate, WarehouseCodeSnapshot = site.Code
            };
            if (await inventory.InsertLedgerAsync(ledger) != 1) throw new InventoryPostingException("库存流水写入失败。");
            results.Add(new InventoryPostingResultDto { CommandItemId = item.CommandItemId, LedgerId = ledger.Uid, BeforeQty = before, Delta = item.QuantityDelta, AfterQty = after });
        }
        return results;
    }

    private static bool IsSamePostingFact(InventoryLedgerEntry existing, Guid commandId, InventoryPostingItem item)
        => existing.CommandId == commandId
            && existing.CommandItemId == item.CommandItemId
            && existing.WarehouseSiteId == item.WarehouseSiteId
            && existing.ProductId == item.ProductId
            && existing.QuantityDelta == item.QuantityDelta
            && existing.TransactionType == item.TransactionType
            && string.Equals(Normalize(existing.SourceDocumentType, 50), Normalize(item.SourceDocumentType, 50), StringComparison.Ordinal)
            && existing.SourceDocumentId == item.SourceDocumentId
            && existing.SourceLineId == item.SourceLineId
            && existing.ReversalOfLedgerId == item.ReversalOfLedgerId
            && string.Equals(Normalize(existing.Reason, 500), Normalize(item.Reason, 500), StringComparison.Ordinal)
            && (item.OccurredDate == default || existing.OccurredDate == item.OccurredDate);

    private static string? Normalize(string? text, int max) { var value = text?.Trim(); return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, max)]; }
}

public sealed class InventoryPostingException(string message) : Exception(message);

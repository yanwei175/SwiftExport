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

        var results = new List<InventoryPostingResultDto>();
        foreach (var item in items.OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.ProductId).ThenBy(x => x.CommandItemId))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = await inventory.FindLedgerByCommandItemAsync(item.CommandItemId);
            if (existing is not null)
            {
                if (existing.CommandId != commandId || existing.WarehouseSiteId != item.WarehouseSiteId || existing.ProductId != item.ProductId || existing.QuantityDelta != item.QuantityDelta || existing.TransactionType != item.TransactionType)
                    throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：CommandItemId 已用于不同库存事实。");
                results.Add(new InventoryPostingResultDto { CommandItemId = item.CommandItemId, LedgerId = existing.Uid, Delta = existing.QuantityDelta, AfterQty = existing.BalanceAfter, BeforeQty = existing.BalanceAfter - existing.QuantityDelta, AlreadyApplied = true });
                continue;
            }
            var site = await warehouseSites.GetForUpdateAsync(item.WarehouseSiteId) ?? throw new InventoryPostingException("虚拟仓不存在。");
            if (!site.IsActive) throw new InventoryPostingException("虚拟仓已停用，不能过账。");
            if (!await inventory.IsActiveProductAsync(item.ProductId)) throw new InventoryPostingException("产品不存在或已停用。");
            var balance = await inventory.LockBalanceAsync(item.WarehouseSiteId, item.ProductId);
            if (balance is null)
            {
                balance = new InventoryBalance { Uid = GuidSequentialGenerator.NewGuid(), WarehouseSiteId = item.WarehouseSiteId, ProductId = item.ProductId, OnHandQty = 0, LastLedgerSequence = 0 };
                if (await inventory.InsertBalanceAsync(balance) != 1) throw new InventoryPostingException("库存余额创建失败。");
            }
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
                SourceDocumentId = item.SourceDocumentId, SourceLineId = item.SourceLineId, CommandId = commandId, CommandItemId = item.CommandItemId,
                Reason = Normalize(item.Reason, 500), OccurredDate = item.OccurredDate == default ? DateTime.Now : item.OccurredDate, WarehouseCodeSnapshot = site.Code
            };
            if (await inventory.InsertLedgerAsync(ledger) != 1) throw new InventoryPostingException("库存流水写入失败。");
            results.Add(new InventoryPostingResultDto { CommandItemId = item.CommandItemId, LedgerId = ledger.Uid, BeforeQty = before, Delta = item.QuantityDelta, AfterQty = after });
        }
        return results;
    }

    private static string? Normalize(string? text, int max) { var value = text?.Trim(); return string.IsNullOrEmpty(value) ? null : value[..Math.Min(value.Length, max)]; }
}

public sealed class InventoryPostingException(string message) : Exception(message);

using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Posting;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;

namespace SwiftExport.Application.Services.Inventory.AutomaticDocuments;

/// <summary>绑定调用方事务的批量资源声明；所有自动写入先取得整组资源，防止分单锁序倒置。</summary>
public sealed class AutomaticPostingScope(ServiceBaseContext context, IAutomaticDocumentRepository automatic,
    IInboundDocumentRepository inbound, IInboundExecutionReadRepository executionReads, IInboundSourceRepository sourceRepository, IOutboundDocumentRepository outbound, IOutboundExecutionReadRepository outboundReads, IInventoryPostingRepository inventory,
    IRepository<WarehouseSite> warehouses) : IAutomaticPostingScope
{
    private IDbTransaction? _transaction;
    private AutomaticPostingResources? _resources;
    private readonly HashSet<Guid> _ledgerIds = [];

    /// <summary>由命令及原流水派生稳定反向明细键，不以重试时间生成新键。</summary>
    public static Guid ReverseItemId(Guid commandId, Guid ledgerId) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"AutomaticReverse:{commandId:N}:{ledgerId:N}"))[..16]);

    /// <inheritdoc/>
    public async Task PrepareAsync(AutomaticPostingResources resources, CancellationToken token = default)
    {
        try
        {
            if (context.Session.Transaction is null) Fail(ServiceErrorCodes.TransactionRequired, "自动单据必须加入调用方事务。");
            if (_transaction is not null) Fail(InventoryErrorCodes.ResourcesAlreadyPrepared, "资源已准备，不能追加或更换事务。");
            if (resources is null || resources.CorrelationId == Guid.Empty || resources.PackingListId == Guid.Empty
                || resources.CommandIds.Count == 0 || resources.CommandIds.Contains(Guid.Empty)
                || resources.CommandIds.Distinct().Count() != resources.CommandIds.Count
                || resources.StockKeys.Any(x => x.WarehouseSiteId == Guid.Empty || x.ProductId == Guid.Empty))
                Fail(ServiceErrorCodes.Validation, "资源声明的命令、箱单或库存身份无效。");
            if (!await automatic.IsSchemaReadyAsync(token)) Fail(ServiceErrorCodes.SchemaNotReady, "自动单据数据库结构尚未兼容，请先核对部署。");
            // 复制集合，防止准备后由调用方修改声明绕过资源保护。
            var r = new AutomaticPostingResources
            {
                CorrelationId = resources!.CorrelationId, PackingListId = resources.PackingListId,
                CommandIds = resources.CommandIds.ToArray(), PurchaseLineIds = resources.PurchaseLineIds.ToArray(),
                InboundDocumentIds = resources.InboundDocumentIds.ToArray(), OutboundDocumentIds = resources.OutboundDocumentIds.ToArray(),
                LedgerIds = resources.LedgerIds.ToArray(), CommandItemIds = resources.CommandItemIds.ToArray(), StockKeys = resources.StockKeys.ToArray()
            };
            foreach (var id in r.CommandIds.Order())
            {
                if (await inventory.AcquireCommandLockAsync(id, token) < 0) Fail(ServiceErrorCodes.CommandBusy, "库存命令正在处理中，请原命令重试。");
            }
            await sourceRepository.LockPurchaseSourcesAsync(r.PurchaseLineIds, token);
            foreach (var id in r.InboundDocumentIds.Distinct().Order())
            {
                await inbound.LockHeaderAsync(id, token);
                var lines = await inbound.LockLinesAsync(id, token);
                if (lines.Any(x => !r.PurchaseLineIds.Contains(x.SourceLineId)))
                    Fail(InventoryErrorCodes.UndeclaredResource, "已有入库单的采购来源必须在准备前完整声明。", id);
            }
            foreach (var id in r.OutboundDocumentIds.Distinct().Order())
            {
                await outbound.LockHeaderAsync(id, token);
                await outbound.LockLinesAsync(id, token);
            }
            var history = new List<InventoryLedgerEntry>();
            foreach (var id in r.InboundDocumentIds.Distinct().Order()) history.AddRange(await executionReads.ReadReverseLedgersAsync(id, token));
            foreach (var id in r.OutboundDocumentIds.Distinct().Order()) history.AddRange(await outboundReads.ReadReverseLedgersAsync(id, token));
            _ledgerIds.UnionWith(r.LedgerIds.Concat(history.Select(x => x.Uid)));
            foreach (var id in _ledgerIds.Order())
            {
                await inventory.LockLedgerByIdAsync(id);
                await inventory.LockReversalByOriginalAsync(id);
                await inventory.LockReplacementByOriginalAsync(id);
            }
            var itemIds = r.CommandItemIds.Concat(_ledgerIds.SelectMany(id => r.CommandIds.Select(c => ReverseItemId(c, id))));
            foreach (var id in itemIds.Distinct().Order()) await inventory.LockLedgerByCommandItemAsync(id);
            foreach (var id in r.StockKeys.Select(x => x.WarehouseSiteId).Distinct().Order()) await warehouses.GetByIdAsync(id, true);
            await automatic.LockProductsAsync(r.StockKeys.Select(x => x.ProductId).Distinct().Order().ToArray(), token);
            foreach (var key in r.StockKeys.Distinct().OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.ProductId))
                await inventory.LockBalanceAsync(key.WarehouseSiteId, key.ProductId);
            token.ThrowIfCancellationRequested();
            _resources = r;
            _transaction = context.Session.Transaction;
        }
        catch { Abort(); throw; }
    }

    /// <summary>检查当前调用属于已准备的同一事务及外层业务，不允许局部失败后继续。</summary>
    internal void Require(Guid commandId, Guid correlationId, Guid packingListId)
    {
        if (_transaction is null || context.Session.Transaction != _transaction || _resources is null)
            Fail(InventoryErrorCodes.ResourcesNotPrepared, "必须先在当前事务准备完整自动过账资源。");
        if (!_resources!.CommandIds.Contains(commandId) || _resources.CorrelationId != correlationId || _resources.PackingListId != packingListId)
            Fail(InventoryErrorCodes.UndeclaredResource, "命令或箱单不属于本次已声明资源。");
    }

    /// <summary>检查单据和每条库存键、来源、流水键均已声明。</summary>
    internal void RequireGroup(AutomaticDocumentGroup group, bool isInbound)
    {
        RequireDocument(group.GroupId, isInbound);
        foreach (var line in group.Lines)
        {
            RequireStock(group.WarehouseSiteId, line.ProductId);
            if (!_resources!.CommandItemIds.Contains(line.LineId)
                || (isInbound && ! _resources.PurchaseLineIds.Contains(line.PurchaseLineId ?? Guid.Empty)))
                Fail(InventoryErrorCodes.UndeclaredResource, "自动明细或采购来源未提前声明。", line.LineId);
        }
    }

    /// <summary>校验单据已在本次范围内声明，拒绝临时追加未锁定单据。</summary>
    internal void RequireDocument(Guid id, bool isInbound)
    {
        if (!(isInbound ? _resources!.InboundDocumentIds : _resources!.OutboundDocumentIds).Contains(id))
            Fail(InventoryErrorCodes.UndeclaredResource, "自动单据未提前声明。", id);
    }

    /// <summary>库存键必须属于预先声明并准备的资源集合。</summary>
    internal void RequireStock(Guid warehouse, Guid product)
    {
        if (!_resources!.StockKeys.Contains(new(warehouse, product))) Fail(InventoryErrorCodes.UndeclaredResource, "库存键未提前声明。");
    }

    /// <summary>反向操作只能访问当前范围已锁定的原流水。</summary>
    internal void RequireLedger(Guid ledgerId)
    {
        if (!_ledgerIds.Contains(ledgerId)) Fail(InventoryErrorCodes.UndeclaredResource, "原流水未提前锁定。", ledgerId);
    }

    /// <summary>使用当前会话既有分类器识别数据库明确终止的序列化事务，供端口保留原异常交给外层；不猜测、不重试。</summary>
    internal bool IsSerializationFailure(Exception error) =>
        context.Session.Errors.Classify(error) == SwiftExport.Core.Interfaces.Data.DatabaseFailureKind.SerializationFailure;

    /// <summary>沿用库存内核失败语义，回滚并清除事务，禁止调用方误提交先前写入。</summary>
    internal void Abort()
    {
        try { context.Session.Transaction?.Rollback(); } catch { }
        context.Session.Transaction = null;
        _resources = null;
        _transaction = null;
    }

    [System.Diagnostics.CodeAnalysis.DoesNotReturn]
    internal static void Fail(string code, string message, Guid? id = null) => throw new AutomaticPostingException(code, message, id);
}

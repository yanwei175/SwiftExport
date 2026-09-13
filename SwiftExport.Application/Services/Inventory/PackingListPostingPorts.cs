using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>自动入库待重构端口；明确失败，由 PackingList 外层捕获并回滚事务。</summary>
public sealed class InboundPostingPort : IInboundPostingPort
{
    /// <summary>阻断旧自动入库，不生成假单据或库存流水。</summary>
    public Task<IReadOnlyList<PackingListGeneratedDocumentDto>> PostAutoReceiptsAsync(
        Guid packingListId, string packingListNo, Guid commandId, DateTime businessDate,
        IReadOnlyList<PackingListPostingPortLine> lines, CancellationToken cancellationToken = default)
        => Task.FromException<IReadOnlyList<PackingListGeneratedDocumentDto>>(new InventoryPostingException("待重构"));

    /// <summary>阻断旧自动入库反向，避免依赖链未重构时部分提交。</summary>
    public Task ReverseAutoReceiptsAsync(Guid packingListId, Guid commandId, string reason, CancellationToken cancellationToken = default)
        => Task.FromException(new InventoryPostingException("待重构"));
}

public sealed class OutboundPostingPort(
    IOutboundDocumentRepository documents,
    IInventoryPostingService inventoryPosting,
    IDocumentNoService documentNos,
    ICurrentUser currentUser) : IOutboundPostingPort
{
    public async Task<IReadOnlyList<PackingListGeneratedDocumentDto>> PostAsync(
        Guid packingListId, string packingListNo, Guid commandId, DateTime businessDate,
        IReadOnlyList<PackingListPostingPortLine> finalShipmentLines,
        IReadOnlyList<PackingListPostingPortLine> customerSuppliedLines,
        CancellationToken cancellationToken = default)
    {
        var generated = new List<PackingListGeneratedDocumentDto>();
        generated.AddRange(await PostTypeAsync(packingListId, packingListNo, commandId, businessDate, finalShipmentLines,
            OutboundDocumentTypeEnum.FinalCustomerShipment, InventoryTransactionTypeEnum.FinalCustomerShipmentOutbound, "FinalOutbound", cancellationToken));
        generated.AddRange(await PostTypeAsync(packingListId, packingListNo, commandId, businessDate, customerSuppliedLines,
            OutboundDocumentTypeEnum.CustomerSuppliedConsumption, InventoryTransactionTypeEnum.CustomerSuppliedConsumptionOutbound, "CustomerSuppliedConsumption", cancellationToken));
        return generated;
    }

    private async Task<IReadOnlyList<PackingListGeneratedDocumentDto>> PostTypeAsync(
        Guid packingListId, string packingListNo, Guid commandId, DateTime businessDate, IReadOnlyList<PackingListPostingPortLine> lines,
        OutboundDocumentTypeEnum documentType, InventoryTransactionTypeEnum transactionType, string kind, CancellationToken cancellationToken)
    {
        var generated = new List<PackingListGeneratedDocumentDto>();
        foreach (var group in lines.Where(x => x.Quantity > 0).GroupBy(x => x.WarehouseSiteId).OrderBy(x => x.Key))
        {
            var documentCommandId = PostingPortIds.Create(commandId, group.Key, $"OUTBOUND_{(int)documentType}");
            var existing = await documents.GetByCommandForUpdateAsync(documentCommandId);
            if (existing is not null)
            {
                if (existing.SourcePackingListId != packingListId || existing.WarehouseSiteId != group.Key || existing.DocumentType != documentType)
                    throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：出库命令已用于不同业务事实。");
                generated.Add(new PackingListGeneratedDocumentDto { DocumentKind = kind, Uid = existing.Uid, DocumentNo = existing.DocumentNo });
                continue;
            }
            var first = group.First();
            var noResult = await documentNos.GetNextDocumentNoAsync(new DocumentNoRequest { DocType = "OB", BusinessDate = businessDate, Operator = currentUser.UserName });
            if (!noResult.Success || noResult.Data is null) throw new InventoryPostingException("出库单号生成失败。");
            var document = new OutboundDocument
            {
                Uid = GuidSequentialGenerator.NewGuid(),
                DocumentNo = noResult.Data.DocumentNo,
                DocumentType = documentType,
                LifecycleStatus = InventoryDocumentLifecycleStatusEnum.Active,
                PostingStatus = OutboundPostingStatusEnum.Posted,
                WarehouseSiteId = group.Key,
                WarehouseCodeSnapshot = first.WarehouseCode,
                WarehouseNameSnapshot = first.WarehouseName,
                ShipmentDate = businessDate,
                SourcePackingListId = packingListId,
                PackingListNoSnapshot = packingListNo,
                PostedDate = DateTime.Now,
                PostedBy = currentUser.UserName,
                CommandId = documentCommandId
            };
            var detail = group.Select((x, index) => new OutboundDocumentLine
            {
                Uid = GuidSequentialGenerator.NewGuid(),
                OutboundDocumentId = document.Uid,
                LineNumber = index + 1,
                ProductId = x.ProductId,
                SkuSnapshot = x.Sku,
                DescriptionSnapshot = x.Description,
                UnitSnapshot = x.Unit,
                Quantity = x.Quantity,
                PackingListContributionId = x.ContributionId,
                NetDemandAllocationId = x.NetDemandAllocationId,
                GrossDemandId = x.GrossDemandId,
                SalesOrderNoSnapshot = x.SalesOrderNo,
                CommandItemId = x.CommandItemId
            }).ToList();
            if (await documents.InsertAsync(document, detail) != 1 + detail.Count)
                throw new InventoryPostingException("PackingList 出库单写入失败。");
            var postingItems = detail.Select(x => new InventoryPostingItem
            {
                CommandItemId = x.CommandItemId,
                WarehouseSiteId = document.WarehouseSiteId,
                ProductId = x.ProductId,
                SkuSnapshot = x.SkuSnapshot,
                ProductNameSnapshot = x.DescriptionSnapshot,
                UnitSnapshot = x.UnitSnapshot,
                QuantityDelta = -x.Quantity,
                TransactionType = transactionType,
                SourceDocumentType = "OutboundDocument",
                SourceDocumentId = document.Uid,
                SourceLineId = x.Uid,
                SourceDocumentNoSnapshot = document.DocumentNo,
                OccurredDate = businessDate
            }).ToList();
            await inventoryPosting.PostAsync(commandId, postingItems, cancellationToken);
            generated.Add(new PackingListGeneratedDocumentDto { DocumentKind = kind, Uid = document.Uid, DocumentNo = document.DocumentNo });
        }
        return generated;
    }

    public async Task ReverseAsync(Guid packingListId, Guid commandId, string reason, CancellationToken cancellationToken = default)
    {
        var outboundDocuments = await documents.GetByPackingListForUpdateAsync(packingListId);
        foreach (var document in outboundDocuments
                     .Where(x => x.DocumentType is OutboundDocumentTypeEnum.FinalCustomerShipment or OutboundDocumentTypeEnum.CustomerSuppliedConsumption)
                     .Where(x => x.PostingStatus == OutboundPostingStatusEnum.Posted)
                     .OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.DocumentType).ThenBy(x => x.Uid))
        {
            var ledgers = await documents.GetLedgersAsync(document.Uid, cancellationToken);
            var occurredDate = DateTime.Now;
            var items = ledgers.Where(x => x.QuantityDelta < 0).Select(x => new InventoryReverseItem
            {
                CommandItemId = PostingPortIds.Create(commandId, x.Uid, "OUTBOUND_REVERSE"),
                OriginalLedgerId = x.Uid,
                SourceDocumentType = "OutboundDocumentReverse",
                SourceDocumentId = document.Uid,
                SourceLineId = x.SourceLineId,
                SourceDocumentNoSnapshot = document.DocumentNo,
                Reason = reason,
                OccurredDate = occurredDate
            }).ToList();
            await inventoryPosting.ReverseAsync(commandId, items, cancellationToken);
            if (await documents.SetReversedAsync(document.Uid, document.Version, DateTime.Now, currentUser.UserName, reason) != 1)
                throw new InventoryPostingException("出库反向发生并发冲突。");
        }
    }
}

internal static class PostingPortIds
{
    public static Guid Create(Guid commandId, Guid sourceId, string discriminator)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{commandId:N}|{sourceId:N}|{discriminator}"));
        var guid = new byte[16];
        Array.Copy(bytes, guid, guid.Length);
        return new Guid(guid);
    }
}

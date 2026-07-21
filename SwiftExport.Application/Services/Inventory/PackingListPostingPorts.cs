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

public sealed class InboundPostingPort(
    IInboundDocumentRepository documents,
    IInventoryPostingService inventoryPosting,
    IDocumentNoService documentNos,
    ICurrentUser currentUser) : IInboundPostingPort
{
    public async Task<IReadOnlyList<PackingListGeneratedDocumentDto>> PostAutoReceiptsAsync(
        Guid packingListId, string packingListNo, Guid commandId, DateTime businessDate,
        IReadOnlyList<PackingListPostingPortLine> lines, CancellationToken cancellationToken = default)
    {
        var generated = new List<PackingListGeneratedDocumentDto>();
        foreach (var group in lines.Where(x => x.Quantity > 0).GroupBy(x => x.WarehouseSiteId).OrderBy(x => x.Key))
        {
            var documentCommandId = PostingPortIds.Create(commandId, group.Key, "INBOUND_DOCUMENT");
            var existing = await documents.GetByCommandForUpdateAsync(documentCommandId);
            if (existing is not null)
            {
                if (existing.SourcePackingListId != packingListId || existing.WarehouseSiteId != group.Key || existing.DocumentType != InboundDocumentTypeEnum.PackingListAutoReceipt)
                    throw new InventoryPostingException("IDEMPOTENCY_CONFLICT：自动入库命令已用于不同业务事实。");
                generated.Add(new PackingListGeneratedDocumentDto { DocumentKind = "AutoInbound", Uid = existing.Uid, DocumentNo = existing.DocumentNo });
                continue;
            }

            var first = group.First();
            var no = await NextNoAsync("IR", businessDate);
            var document = new InboundDocument
            {
                Uid = GuidSequentialGenerator.NewGuid(),
                DocumentNo = no,
                DocumentType = InboundDocumentTypeEnum.PackingListAutoReceipt,
                LifecycleStatus = InventoryDocumentLifecycleStatusEnum.Active,
                PostingStatus = InboundPostingStatusEnum.Posted,
                WarehouseSiteId = group.Key,
                WarehouseCodeSnapshot = first.WarehouseCode,
                WarehouseNameSnapshot = first.WarehouseName,
                SupplierId = first.SupplierId,
                SupplierNameSnapshot = first.SupplierName,
                ReceiptDate = businessDate,
                Remark = $"PackingList {packingListNo} 自动补入库",
                SourcePackingListId = packingListId,
                PostedDate = DateTime.Now,
                PostedBy = currentUser.UserName,
                CommandId = documentCommandId
            };
            var documentLines = group.Select((x, index) => new InboundDocumentLine
            {
                Uid = GuidSequentialGenerator.NewGuid(),
                InboundDocumentId = document.Uid,
                LineNumber = index + 1,
                ProductId = x.ProductId,
                SkuSnapshot = x.Sku,
                DescriptionSnapshot = x.Description,
                UnitSnapshot = x.Unit,
                Quantity = x.Quantity,
                SourceType = InboundSourceTypeEnum.PackingListContribution,
                SourceUid = x.ContributionId,
                NetDemandAllocationId = x.NetDemandAllocationId,
                GrossDemandId = x.GrossDemandId,
                SalesOrderNoSnapshot = x.SalesOrderNo
            }).ToList();
            if (await documents.InsertAsync(document, documentLines) != 1 + documentLines.Count)
                throw new InventoryPostingException("PackingList 自动入库单写入失败。");
            var postingItems = documentLines.Select((x, index) => new InventoryPostingItem
            {
                CommandItemId = group.ElementAt(index).CommandItemId,
                WarehouseSiteId = document.WarehouseSiteId,
                ProductId = x.ProductId,
                QuantityDelta = x.Quantity,
                TransactionType = InventoryTransactionTypeEnum.PackingListAutoReceiptInbound,
                SourceDocumentType = "InboundDocument",
                SourceDocumentId = document.Uid,
                SourceLineId = x.Uid,
                OccurredDate = businessDate
            }).ToList();
            await inventoryPosting.PostAsync(commandId, postingItems, cancellationToken);
            generated.Add(new PackingListGeneratedDocumentDto { DocumentKind = "AutoInbound", Uid = document.Uid, DocumentNo = document.DocumentNo });
        }
        return generated;
    }

    public async Task ReverseAutoReceiptsAsync(Guid packingListId, Guid commandId, string reason, CancellationToken cancellationToken = default)
    {
        var inboundDocuments = await documents.GetByPackingListForUpdateAsync(packingListId);
        foreach (var document in inboundDocuments
                     .Where(x => x.DocumentType == InboundDocumentTypeEnum.PackingListAutoReceipt && x.PostingStatus == InboundPostingStatusEnum.Posted)
                     .OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.Uid))
        {
            var ledgers = await documents.GetLedgersAsync(document.Uid, cancellationToken);
            var items = ledgers.Where(x => x.QuantityDelta > 0).Select(x => new InventoryPostingItem
            {
                CommandItemId = PostingPortIds.Create(commandId, x.Uid, "INBOUND_REVERSE"),
                WarehouseSiteId = x.WarehouseSiteId,
                ProductId = x.ProductId,
                QuantityDelta = -x.QuantityDelta,
                TransactionType = InventoryTransactionTypeEnum.Reversal,
                SourceDocumentType = "InboundDocumentReverse",
                SourceDocumentId = document.Uid,
                SourceLineId = x.SourceLineId,
                ReversalOfLedgerId = x.Uid,
                Reason = reason,
                OccurredDate = DateTime.Now
            }).ToList();
            await inventoryPosting.PostAsync(commandId, items, cancellationToken);
            if (await documents.SetReversedAsync(document.Uid, document.Version, DateTime.Now, currentUser.UserName, reason, commandId) != 1)
                throw new InventoryPostingException("自动入库反向发生并发冲突。");
        }
    }

    private async Task<string> NextNoAsync(string type, DateTime date)
    {
        var result = await documentNos.GetNextDocumentNoAsync(new DocumentNoRequest { DocType = type, BusinessDate = date, Operator = currentUser.UserName });
        if (!result.Success || result.Data is null) throw new InventoryPostingException("单据编号生成失败。");
        return result.Data.DocumentNo;
    }
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
                QuantityDelta = -x.Quantity,
                TransactionType = transactionType,
                SourceDocumentType = "OutboundDocument",
                SourceDocumentId = document.Uid,
                SourceLineId = x.Uid,
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
            var items = ledgers.Where(x => x.QuantityDelta < 0).Select(x => new InventoryPostingItem
            {
                CommandItemId = PostingPortIds.Create(commandId, x.Uid, "OUTBOUND_REVERSE"),
                WarehouseSiteId = x.WarehouseSiteId,
                ProductId = x.ProductId,
                QuantityDelta = -x.QuantityDelta,
                TransactionType = InventoryTransactionTypeEnum.Reversal,
                SourceDocumentType = "OutboundDocumentReverse",
                SourceDocumentId = document.Uid,
                SourceLineId = x.SourceLineId,
                ReversalOfLedgerId = x.Uid,
                Reason = reason,
                OccurredDate = DateTime.Now
            }).ToList();
            await inventoryPosting.PostAsync(commandId, items, cancellationToken);
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

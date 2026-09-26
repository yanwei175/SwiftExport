using SwiftExport.Application.Interfaces.Inventory.AutomaticDocuments;
using System.Security.Cryptography;
using System.Text;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>把已验证预览转换为内部自动单据命令；不执行命令、不启动事务。</summary>
public sealed class PackingListAutomaticCommandBuilder
{
    /// <summary>已形成的入出库批次及其资源声明，保留请求行与原预览动作对应关系。</summary>
    public sealed record Commands(AutomaticPostingRequest? Inbound, AutomaticPostingRequest? Outbound,
        AutomaticPostingResources? Resources, IReadOnlyDictionary<Guid, PackingListPostingPreviewBuilder.Action> LineActions);

    /// <summary>稳定派生子身份，同一外层命令重试不会换单据、行或日志键。</summary>
    public static Guid DeriveId(Guid commandId, string purpose) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"PackingList:v1:{commandId:N}:{purpose}"))[..16]);

    /// <summary>仅接受无阻断方案；候选全集由查询提供，用于首次库存写入前的资源锁定。</summary>
    public Commands Build(Guid commandId, Guid packingListId, string packingListNo, DateTime businessDate,
        PackingListPostingPreviewBuilder.Preview preview, IReadOnlyList<AutomaticStockKey> candidateStocks,
        IReadOnlyList<Guid> candidatePurchaseLines)
    {
        if (commandId == Guid.Empty || packingListId == Guid.Empty || string.IsNullOrWhiteSpace(packingListNo)
            || preview.Token is null || preview.Blockers.Count != 0)
            throw new ArgumentException("只有无阻断预览才能建立自动单据命令。");
        if (preview.Actions.Any(x => x.Kind is not ("Delivery" or "Receipt" or "Shipment" or "Consumption")))
            throw new ArgumentException("预览包含未知库存动作。");
        var rows = preview.Actions.Where(x => x.Kind != "Delivery")
            .OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.WarehouseId).ThenBy(x => x.PackingListLineId)
            .ThenBy(x => x.GrossDemandId).ThenBy(x => x.ComponentId).ThenBy(x => x.PurchaseLineId).ToArray();
        if (rows.Length == 0) return new(null, null, null, new Dictionary<Guid, PackingListPostingPreviewBuilder.Action>());
        foreach (var row in rows)
        {
            if (row.ProductId is null || row.WarehouseId is null || row.Qty <= 0 || string.IsNullOrWhiteSpace(row.Unit)
                || !candidateStocks.Contains(new(row.WarehouseId.Value, row.ProductId.Value))
                || (row.Kind == "Receipt" && (row.PurchaseLineId is null || !candidatePurchaseLines.Contains(row.PurchaseLineId.Value))))
                throw new ArgumentException("预览动作缺少产品、仓库、单位、正数量或预声明资源。");
        }
        var map = new Dictionary<Guid, PackingListPostingPreviewBuilder.Action>();
        var inbound = BuildRequest(true);
        var outbound = BuildRequest(false);
        var requests = new[] { inbound, outbound }.OfType<AutomaticPostingRequest>().ToArray();
        return new(inbound, outbound, new AutomaticPostingResources
        {
            CorrelationId = commandId,
            PackingListId = packingListId,
            CommandIds = requests.Select(x => x.CommandId).Order().ToArray(),
            PurchaseLineIds = candidatePurchaseLines.Distinct().Order().ToArray(),
            StockKeys = candidateStocks.Distinct().OrderBy(x => x.WarehouseSiteId).ThenBy(x => x.ProductId).ToArray(),
            InboundDocumentIds = inbound?.Groups.Select(x => x.GroupId).ToArray() ?? [],
            OutboundDocumentIds = outbound?.Groups.Select(x => x.GroupId).ToArray() ?? [],
            CommandItemIds = map.Keys.Order().ToArray()
        }, map);

        // 同产品不同依据保留各自物理行，不受手工出库产品唯一规则影响。
        AutomaticPostingRequest? BuildRequest(bool isInbound)
        {
            var groups = rows.Where(x => (x.Kind == "Receipt") == isInbound)
                .GroupBy(x => (x.Kind, Warehouse: x.WarehouseId!.Value)).ToArray();
            if (groups.Length == 0) return null;
            return new AutomaticPostingRequest
            {
                CommandId = DeriveId(commandId, isInbound ? "inbound" : "outbound"),
                CorrelationId = commandId, PackingListId = packingListId, PackingListNo = packingListNo.Trim(),
                BusinessDate = businessDate.Date,
                Groups = groups.Select(group => new AutomaticDocumentGroup
                {
                    GroupId = DeriveId(commandId, $"document:{group.Key.Kind}:{group.Key.Warehouse:N}"),
                    WarehouseSiteId = group.Key.Warehouse,
                    OutboundType = isInbound ? null : group.Key.Kind == "Shipment"
                        ? OutboundDocumentTypeEnum.FinalCustomerShipment : OutboundDocumentTypeEnum.CustomerSuppliedConsumption,
                    Lines = group.Select((row, index) =>
                    {
                        var id = DeriveId(commandId, $"line:{group.Key.Kind}:{group.Key.Warehouse:N}:{index}");
                        map.Add(id, row);
                        return new AutomaticDocumentLine { LineId = id, BasisId = id, PurchaseLineId = row.PurchaseLineId,
                            ProductId = row.ProductId!.Value, Unit = row.Unit.Trim().ToUpperInvariant(), Quantity = row.Qty };
                    }).ToArray()
                }).ToArray()
            };
        }
    }
}

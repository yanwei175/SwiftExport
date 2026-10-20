using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Core.Interfaces.Shipping;
using static SwiftExport.Application.Services.Shipping.PackingListPostingPreviewBuilder;

namespace SwiftExport.Application.Services.Shipping;

/// <summary>把领域原始事实解析成预览输入；不查当前 BOM、不修改原实体。</summary>
public sealed class PackingListPostingFactsResolver
{
    /// <summary>预览、全部候选库存和采购行；调用方据此声明资源后重读重算。</summary>
    public sealed record Resolved(Preview Preview, IReadOnlyList<Stock> Stocks, IReadOnlyList<Receipt> Receipts,
        IReadOnlyList<Guid> PurchaseLineIds);

    /// <summary>校验真实根子及 Link 归属，复用入库模块已计算的采购可收资格和净收货容量。</summary>
    public Resolved Resolve(PackingListPostingFacts facts, IReadOnlyList<InboundPurchasePostingSourceDto> receiptFacts,
        DateTime businessDate, bool hasUnprovenReceipt, CancellationToken token = default)
    {
        var blockers = new List<Blocker>();
        if (facts.Head is null) return Failed(new(ServiceErrorCodes.NotFound, null, "箱单不存在。"));
        if (!facts.CustomerExists) return Failed(new(PackingListErrorCodes.CustomerInvalid, null, "箱单客户不存在或已删除。"));
        var products = facts.Products.ToDictionary(x => x.Uid);
        var purchases = facts.PurchaseLines.ToDictionary(x => x.Uid);
        var purchaseHeads = facts.PurchaseHeads.ToDictionary(x => x.Uid);
        if (facts.Lines.Any(x => !x.IsDeleted && !x.GrossDemandId.HasValue))
            return Failed(new(PackingListErrorCodes.SourceNotAssociated, null, "箱单尚有待关联来源行，请先分配全部数量。"));
        var normalized = new List<Source>();
        var allPurchaseIds = new HashSet<Guid>();
        var stockProducts = new HashSet<Guid>();
        foreach (var source in facts.Sources.OrderBy(x => x.Uid))
        {
            token.ThrowIfCancellationRequested();
            var deliveryOnly = source.IsNonStandard || source.SourceType == DocumentTypeStaticsEnum.ComplaintReplenishment;
            var unit = source.UnitSnapshot?.Trim() ?? "";
            var roots = facts.Demands.Where(x => x.GrossDemandId == source.Uid && !x.IsDeleted
                && x.LifecycleStatus == NetDemandLifecycleStatusEnum.Active && x.ParentNetDemandId == null).ToArray();
            var valid = !source.IsDeleted && source.LifecycleStatus == GrossDemandLifecycleStatusEnum.Active;
            if (deliveryOnly)
            {
                normalized.Add(new(source.Uid, source.CustomerId ?? Guid.Empty, source.ProductId, unit, valid, true,
                    source.DemandQty - facts.Delivered.GetValueOrDefault(source.Uid), null, 0, source.Version, [], [], []));
                continue;
            }
            if (source.ProductId is not { } productId || !products.TryGetValue(productId, out var product)
                || product.IsDeleted || string.IsNullOrWhiteSpace(product.Unit) || !SameUnit(product.Unit, unit))
            {
                blockers.Add(new(PackingListErrorCodes.ProductInvalid, source.Uid, "来源产品不存在、已删除或计量单位不一致。"));
                continue;
            }
            if (roots.Length != 1 || roots[0].RootNetDemandId != roots[0].Uid || roots[0].ProductId != productId
                || roots[0].GrossQty <= 0)
            {
                blockers.Add(new(PackingListErrorCodes.DemandInvalid, source.Uid, "来源必须具有唯一有效根物料需求。"));
                continue;
            }
            var root = roots[0];
            if (string.IsNullOrWhiteSpace(source.DemandBasisFingerprint)
                || root.SourceDemandBasisFingerprintSnapshot != source.DemandBasisFingerprint)
            {
                blockers.Add(new(PackingListErrorCodes.DemandStale, source.Uid, "物料拆解不能证明与当前来源实物要求一致。"));
                continue;
            }
            var tree = facts.Demands.Where(x => x.GrossDemandId == source.Uid && !x.IsDeleted
                && x.LifecycleStatus == NetDemandLifecycleStatusEnum.Active).ToArray();
            if (tree.Any(x => x.RootNetDemandId != root.Uid || x.DecompositionRevision != root.DecompositionRevision))
            {
                blockers.Add(new(PackingListErrorCodes.DemandAmbiguous, source.Uid, "存在跨根或跨拆解批次的活动物料需求。"));
                continue;
            }
            var links = facts.Links.Where(x => x.NetDemandId == root.Uid && !x.IsDeleted
                && x.LinkStatus == DemandExecutionLinkStatusEnum.Active && x.AllocatedQty > x.ReleasedQty).ToArray();
            var linkedPurchases = new List<Guid>();
            var supplierIds = new HashSet<Guid>();
            foreach (var link in links)
            {
                if (link.TargetType != "PO" || !purchases.TryGetValue(link.TargetLineId, out var line)
                    || line.PurchaseOrderId != link.TargetHeadId || line.ProductId != productId || line.IsDeleted
                    || !purchaseHeads.TryGetValue(line.PurchaseOrderId, out var po) || po.IsDeleted
                    || po.ConfirmationStatus!=ConfirmationStatusEnum.已确认 || po.LifecycleStatus!=PurchaseOrderLifecycleStatusEnum.Active)
                {
                    blockers.Add(new(PackingListErrorCodes.ExecutionLinkInvalid, source.Uid, "执行关系的采购目标、产品或归属无效。"));
                    continue;
                }
                linkedPurchases.Add(line.Uid);
                supplierIds.Add(po.SupplierId);
                allPurchaseIds.Add(line.Uid);
            }
            var components = new List<Component>();
            foreach (var child in tree.Where(x => x.Uid != root.Uid))
            {
                if (child.MaterialTypeSnapshot != MaterialTypesEnum.客供件 || child.ParentNetDemandId != root.Uid
                    || child.DeliveryTargetNetDemandId != root.Uid || child.DeliveryMode != DemandDeliveryModeEnum.ParentExecutionSupplier
                    || child.ProductId is not { } childId || !products.TryGetValue(childId, out var childProduct)
                    || childProduct.IsDeleted || string.IsNullOrWhiteSpace(childProduct.Unit) || child.GrossQty < 0)
                {
                    blockers.Add(new(PackingListErrorCodes.ComponentInvalid, source.Uid, "客供件归属、送达关系、产品或数量快照无效。"));
                    continue;
                }
                components.Add(new(child.Uid, childId, childProduct.Unit.Trim(), child.GrossQty, child.Version));
                stockProducts.Add(childId);
            }
            stockProducts.Add(productId);
            normalized.Add(new(source.Uid, source.CustomerId ?? Guid.Empty, productId, unit, valid, false,
                source.DemandQty - facts.Delivered.GetValueOrDefault(source.Uid), root.Uid, root.GrossQty, source.Version,
                linkedPurchases.Distinct().Order().ToArray(), facts.Warehouses.Where(x => x.SupplierId.HasValue
                    && supplierIds.Contains(x.SupplierId.Value)).Select(x => x.Uid).Order().ToArray(), components));
        }
        if (blockers.Count != 0) return new(new(null, [], blockers), [], [], allPurchaseIds.Order().ToArray());
        var stocks = new List<Stock>();
        foreach (var product in stockProducts.Order())
        foreach (var warehouse in facts.Warehouses.Where(x => !x.IsDeleted && x.IsActive).OrderBy(x => x.Uid))
        {
            var balances = facts.Balances.Where(x => x.ProductId == product && x.WarehouseSiteId == warehouse.Uid).ToArray();
            if (balances.Length > 1 || balances.Any(x => x.IsDeleted || !SameUnit(x.UnitSnapshot, products[product].Unit)))
            {
                blockers.Add(new(PackingListErrorCodes.BalanceInvalid, null, "库存投影重复、已删除或单位与产品不一致。"));
                continue;
            }
            var balance = balances.SingleOrDefault();
            stocks.Add(new(PackingListAutomaticCommandBuilder.DeriveId(warehouse.Uid, $"stock:{product:N}"),
                warehouse.Uid, warehouse.Code, product, products[product].Unit!, balance?.OnHandQty ?? 0, balance?.Version ?? 0));
        }
        var receipts = new List<Receipt>();
        if (!hasUnprovenReceipt)
        foreach (var receipt in receiptFacts.Where(x => allPurchaseIds.Contains(x.SourceLineId) && x.IsReceivable
            && !x.IsCustomerSupplied && x.OriginalQty > x.PostedNetQty))
        {
            if (!purchases.TryGetValue(receipt.SourceLineId, out var line)
                || !purchaseHeads.TryGetValue(line.PurchaseOrderId, out var po)
                || receipt.ProductId != line.ProductId || !SameUnit(receipt.Unit, receipt.CurrentUnit)) continue;
            var warehouses = facts.Warehouses.Where(x => !x.IsDeleted && x.IsActive
                && x.SiteType == WarehouseSiteTypeEnum.SupplierVirtual && x.SupplierId == po.SupplierId).ToArray();
            // 没有唯一目标仓仅使该新增收货容量不可用；既有库存足够时仍可过账。
            if (warehouses.Length != 1) continue;
            receipts.Add(new(line.Uid, warehouses[0].Uid, warehouses[0].Code, receipt.ProductId, receipt.Unit,
                receipt.OriginalQty - receipt.PostedNetQty, po.OrderNo, line.LineNumber, line.Version));
        }
        if (blockers.Count != 0) return new(new(null, [], blockers), stocks, receipts, allPurchaseIds.Order().ToArray());
        var preview = new PackingListPostingPreviewBuilder().Build(facts.Head, facts.Lines, normalized, stocks, receipts, businessDate, token);
        if (preview.Token != null)
        {
            // 额外纳入实际主数据和 Link 技术版本；不能仅在分配数量变化时才使预览失效。
            var versions = JsonSerializer.Serialize(new { preview.Token,
                Demands = facts.Demands.OrderBy(x => x.Uid), Links = facts.Links.OrderBy(x => x.Uid),
                Products = facts.Products.OrderBy(x => x.Uid), Warehouses = facts.Warehouses.OrderBy(x => x.Uid),
                PurchaseHeads = facts.PurchaseHeads.OrderBy(x => x.Uid), PurchaseLines = facts.PurchaseLines.OrderBy(x => x.Uid),
                ReceiptFacts = receiptFacts.OrderBy(x => x.SourceLineId), hasUnprovenReceipt });
            preview = preview with { Token = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versions))) };
        }
        return new(preview, stocks, receipts, allPurchaseIds.Order().ToArray());
    }

    /// <summary>来源解析失败时不返回可执行的部分动作。</summary>
    private static Resolved Failed(Blocker blocker) => new(new(null, [], [blocker]), [], [], []);
    /// <summary>单位只比较大小写和空白，不隐式换算。</summary>
    private static bool SameUnit(string? a, string? b) => string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}

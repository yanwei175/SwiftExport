using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.DemandPlanning;

/// <summary>采购草稿减量的纯计算器；不访问数据库。</summary>
public sealed class PurchaseDraftReleasePlanner
{
    public PurchaseDraftReleasePreviewResponse Build(
        Guid purchaseOrderId,
        int expectedVersion,
        decimal newTotalQty,
        IReadOnlyList<PurchaseDraftLifecycleLinkFactDto> links)
    {
        ArgumentNullException.ThrowIfNull(links);
        var response = new PurchaseDraftReleasePreviewResponse
        {
            PurchaseOrderId = purchaseOrderId,
            ExpectedVersion = expectedVersion,
            CurrentTotalQty = links.Sum(x => x.EffectiveQty),
            NewTotalQty = newTotalQty,
        };

        if (purchaseOrderId == Guid.Empty) response.Blockers.Add("采购草稿 Uid 不能为空。");
        if (expectedVersion <= 0) response.Blockers.Add("采购草稿版本无效。");
        if (links.Count == 0) response.Blockers.Add("采购草稿没有可释放的新链路贡献。");
        if (links.Any(x => x.LinkId == Guid.Empty || x.NetDemandId == Guid.Empty
            || x.PurchaseOrderLineId == Guid.Empty || x.EffectiveQty < 0))
            response.Blockers.Add("采购草稿 Link 事实不完整。");
        if (newTotalQty < 0) response.Blockers.Add("新数量不能小于 0。");
        if (newTotalQty >= response.CurrentTotalQty)
            response.Blockers.Add("减量后的新数量必须小于当前 Link 有效总量；增加数量不在释放命令范围内。");

        response.ReleaseQty = Math.Max(0, response.CurrentTotalQty - newTotalQty);
        if (response.Blockers.Count == 0)
        {
            var pending = response.ReleaseQty;
            foreach (var link in links
                         .OrderByDescending(x => x.PlannedDeliveryDate ?? DateTime.MinValue)
                         .ThenByDescending(x => x.SourceLineNumber ?? int.MinValue)
                         .ThenByDescending(x => x.NetDemandId)
                         .ThenByDescending(x => x.LinkId))
            {
                if (pending <= 0) break;
                var release = Math.Min(pending, link.EffectiveQty);
                if (release <= 0) continue;
                response.Releases.Add(new PurchaseDraftLinkReleaseDto
                {
                    LinkId = link.LinkId,
                    NetDemandId = link.NetDemandId,
                    HistoricalAllocationId = link.HistoricalAllocationId,
                    PurchaseOrderLineId = link.PurchaseOrderLineId,
                    SourceLineNumber = link.SourceLineNumber,
                    PlannedDeliveryDate = link.PlannedDeliveryDate,
                    CurrentEffectiveQty = link.EffectiveQty,
                    ReleaseQty = release,
                    RemainingEffectiveQty = link.EffectiveQty - release,
                });
                pending -= release;
            }
            if (pending != 0) response.Blockers.Add("Link 有效数量不足，无法完成本次减量。");
        }

        response.HasBlockers = response.Blockers.Count > 0;
        response.PreviewToken = BuildToken(response, links);
        return response;
    }

    private static string BuildToken(
        PurchaseDraftReleasePreviewResponse response,
        IReadOnlyList<PurchaseDraftLifecycleLinkFactDto> links)
    {
        var builder = new StringBuilder();
        builder.Append(response.PurchaseOrderId.ToString("D")).Append('|')
            .Append(response.ExpectedVersion).Append('|')
            .Append(response.NewTotalQty.ToString("0.####", CultureInfo.InvariantCulture));
        foreach (var link in links.OrderBy(x => x.LinkId))
        {
            builder.Append('|').Append(link.LinkId.ToString("D"))
                .Append(':').Append(link.NetDemandId.ToString("D"))
                .Append(':').Append(link.PurchaseOrderLineId.ToString("D"))
                .Append(':').Append(link.EffectiveQty.ToString("0.####", CultureInfo.InvariantCulture))
                .Append(':').Append(link.PlannedDeliveryDate?.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty)
                .Append(':').Append(link.SourceLineNumber?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}

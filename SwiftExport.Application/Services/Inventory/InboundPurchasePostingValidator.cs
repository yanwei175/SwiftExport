using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;

namespace SwiftExport.Application.Services.Inventory;

/// <summary>仅解释采购来源的收货资格及容量，不承担库存写入或其他类型来源规则。</summary>
internal static class InboundPurchasePostingValidator
{
    /// <summary>将采购实时事实与已保存入库行对照，输出可定位的阻断及容量快照。</summary>
    internal static void Validate(InboundManagerDetailDto detail, IReadOnlyList<InboundPurchasePostingSourceDto> sources, InboundPostPreviewDto preview)
    {
        var byId = sources.ToDictionary(x => x.SourceLineId);
        foreach (var line in detail.Lines)
        {
            byId.TryGetValue(line.SourceLineId, out var source);
            preview.Lines.Add(new()
            {
                Uid = line.Uid, SourceLineId = line.SourceLineId, ProductId = line.ProductId,
                LineNumber = line.LineNumber, SourceNo = source?.SourceNo ?? line.SourceNo,
                Sku = line.SkuSnapshot, Unit = line.UnitSnapshot, Quantity = line.Quantity,
                OriginalQty = source?.OriginalQty ?? 0, PostedNetQty = source?.PostedNetQty ?? 0,
                RemainingQty = source is null ? 0 : source.OriginalQty - source.PostedNetQty,
                OtherDraftCount = line.OtherDraftCount, OtherDraftQuantity = line.OtherDraftQuantity
            });
            string? error = null;
            if (source is null || !source.IsReceivable) error = "采购来源不存在、已删除、非活动、未确认或物料类型无效。";
            else if (source.ProductId != line.ProductId || string.IsNullOrWhiteSpace(source.Unit)
                || !SameUnit(source.Unit, line.UnitSnapshot) || !SameUnit(source.Unit, source.CurrentUnit))
                error = "采购来源产品或单位已变化，请修改草稿；基础单位不一致时不能过账。";
            else if (source.PostedNetQty < 0 || source.OriginalQty <= 0) error = "来源采购数量或正式收货净额异常，请先核实历史事实。";
            else if (line.Quantity > source.OriginalQty - source.PostedNetQty)
                error = $"采购剩余可收 {source.OriginalQty - source.PostedNetQty:0.####}，本次 {line.Quantity:0.####}，不能超量入库。";
            if (error is not null) preview.Blockers.Add(new() { Code = ServiceErrorCodes.BusinessBlocked, Message = error, LineId = line.Uid, SourceLineId = line.SourceLineId });
        }
    }

    private static bool SameUnit(string? left, string? right) => !string.IsNullOrWhiteSpace(right)
        && string.Equals(left?.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}

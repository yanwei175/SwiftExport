using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SwiftExport.Application.Interfaces.Inventory.Posting;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Entities.Inventory;

namespace SwiftExport.Application.Services.Inventory.Posting;

/// <summary>保存一次库存事实的规范化请求、来源快照及不可变流水构造依据。</summary>
internal sealed class PostingFact
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

    /// <summary>规范化普通库存请求并计算首个稳定指纹；字段边界保持原有数组顺序。</summary>
    public static PostingFact From(Guid commandId, InventoryPostingItem item)
    {
        var fact = new PostingFact
        {
            CommandId = commandId, CommandItemId = item.CommandItemId,
            WarehouseSiteId = item.WarehouseSiteId, ProductId = item.ProductId,
            QuantityDelta = item.QuantityDelta, TransactionType = item.TransactionType,
            SourceDocumentType = item.SourceDocumentType.Trim(), SourceDocumentId = item.SourceDocumentId,
            SourceLineId = item.SourceLineId, SourceDocumentNoSnapshot = Normalize(item.SourceDocumentNoSnapshot),
            Reason = Normalize(item.Reason), OccurredDate = item.OccurredDate,
            SkuSnapshot = Normalize(item.SkuSnapshot), ProductNameSnapshot = Normalize(item.ProductNameSnapshot),
            UnitSnapshot = Normalize(item.UnitSnapshot)
        };
        fact.RefreshFingerprint();
        return fact;
    }

    /// <summary>在普通事实基础上关联被替代流水与纠错组，并重算包含关联身份的指纹。</summary>
    public static PostingFact FromCorrection(Guid commandId, InventoryPostingItem item, Guid originalId)
    {
        var fact = From(commandId, item);
        fact.ReplacementOfLedgerId = originalId;
        fact.CorrectionGroupId = commandId;
        fact.RefreshFingerprint();
        return fact;
    }

    /// <summary>只从锁定原流水派生反向的库存身份、数量和快照，不接受请求覆盖。</summary>
    public static PostingFact Reverse(Guid commandId, InventoryReverseItem item, InventoryLedgerEntry original, Guid? groupId)
    {
        var fact = new PostingFact
        {
            CommandId = commandId, CommandItemId = item.CommandItemId,
            WarehouseSiteId = original.WarehouseSiteId, ProductId = original.ProductId,
            QuantityDelta = -original.QuantityDelta, TransactionType = InventoryTransactionTypeEnum.Reversal,
            SourceDocumentType = item.SourceDocumentType.Trim(), SourceDocumentId = item.SourceDocumentId,
            SourceLineId = item.SourceLineId, SourceDocumentNoSnapshot = Normalize(item.SourceDocumentNoSnapshot),
            ReversalOfLedgerId = original.Uid, CorrectionGroupId = groupId,
            Reason = Normalize(item.Reason), OccurredDate = item.OccurredDate,
            SkuSnapshot = Normalize(original.SkuSnapshot), ProductNameSnapshot = Normalize(original.ProductNameSnapshot),
            UnitSnapshot = Normalize(original.UnitSnapshot), Original = original
        };
        fact.RefreshFingerprint();
        return fact;
    }

    /// <summary>把已校验的事实转成不可变流水，序号和最终余额由同一事务中的内核提供。</summary>
    public InventoryLedgerEntry ToEntity(long sequence, decimal after, WarehouseSite site, InventoryBalance balance) => new()
    {
        Uid = GuidSequentialGenerator.NewGuid(), Sequence = sequence,
        WarehouseSiteId = WarehouseSiteId, ProductId = ProductId, TransactionType = TransactionType,
        QuantityDelta = QuantityDelta, BalanceAfter = after,
        SourceDocumentType = SourceDocumentType, SourceDocumentId = SourceDocumentId,
        SourceLineId = SourceLineId, SourceDocumentNoSnapshot = SourceDocumentNoSnapshot,
        ReversalOfLedgerId = ReversalOfLedgerId, ReplacementOfLedgerId = ReplacementOfLedgerId,
        CorrectionGroupId = CorrectionGroupId, CommandId = CommandId, CommandItemId = CommandItemId,
        RequestFingerprint = RequestFingerprint, Reason = Reason, OccurredDate = OccurredDate,
        WarehouseCodeSnapshot = site.Code, WarehouseNameSnapshot = site.Name,
        SkuSnapshot = balance.SkuSnapshot, ProductNameSnapshot = balance.ProductNameSnapshot,
        UnitSnapshot = balance.UnitSnapshot
    };
    /// <summary>纠错替代缺省快照从原事实派生，但已计算的请求指纹继续保留原始缺省语义。</summary>
    public void UseOriginalSnapshots(InventoryLedgerEntry original)
    {
        SkuSnapshot = Normalize(SkuSnapshot) ?? Normalize(original.SkuSnapshot);
        ProductNameSnapshot = Normalize(ProductNameSnapshot) ?? Normalize(original.ProductNameSnapshot);
        UnitSnapshot = Normalize(UnitSnapshot) ?? Normalize(original.UnitSnapshot);
    }

    /// <summary>按既有字段顺序和序列化方式生成明细幂等指纹，不改变历史比较口径。</summary>
    private void RefreshFingerprint()
    {
        var canonical = JsonSerializer.Serialize(new object?[]
        {
            CommandId, CommandItemId, WarehouseSiteId, ProductId, QuantityDelta, (int)TransactionType,
            SourceDocumentType, SourceDocumentId, SourceLineId, SourceDocumentNoSnapshot,
            ReversalOfLedgerId, ReplacementOfLedgerId, CorrectionGroupId, Reason, OccurredDate,
            SkuSnapshot, ProductNameSnapshot, UnitSnapshot
        });
        RequestFingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <summary>统一可选快照的空白语义，保持非空值原样修剪。</summary>
    private static string? Normalize(string? value)
    {
        var text = value?.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }
}

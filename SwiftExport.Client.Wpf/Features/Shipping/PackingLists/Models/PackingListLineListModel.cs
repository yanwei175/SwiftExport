using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Mvvm;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>维护装箱单物理行及不混合单位的只读汇总，沿用基类删除池。</summary>
public sealed partial class PackingListLineListModel : BaseListModel<PackingListLineModel, PackingListLineDto>
{
    /// <summary>订阅自身列表状态，覆盖编辑、增删、恢复和排序。</summary>
    public PackingListLineListModel() : base(_ => new PackingListLineModel())
    {
        ListStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InvoiceNosSummary));
            OnPropertyChanged(nameof(QtyByUnit));
            OnPropertyChanged(nameof(QtySummary));
            OnPropertyChanged(nameof(NetWeightSummary));
            OnPropertyChanged(nameof(GrossWeightSummary));
            OnPropertyChanged(nameof(CbmSummary));
            OnPropertyChanged(nameof(CartonSummary));
        };
    }

    /// <summary>有效行按业务行号和身份排序，去空白、区分大小写去重。</summary>
    public string InvoiceNosSummary => string.Join(", ", Items.Where(x => !x.IsDeleted)
        .OrderBy(x => x.LineNumber).ThenBy(x => x.Uid).Select(x => x.InvoiceNo?.Trim())
        .Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal));

    /// <summary>不同单位分别累计数量；空单位独立分组，不猜测单位。</summary>
    public IReadOnlyDictionary<string, decimal> QtyByUnit => Items.Where(x => !x.IsDeleted)
        .GroupBy(x => x.UnitSnapshot ?? string.Empty).ToDictionary(x => x.Key, x => x.Sum(y => y.Qty));

    /// <summary>按当前显示文化输出分单位数量，不生成跨单位总数。</summary>
    public string QtySummary => string.Join("; ", QtyByUnit.Select(x => $"{x.Value:0.####} {x.Key}".Trim()));

    /// <summary>任一有效行未知时净重合计保持未知。</summary>
    public decimal? NetWeightSummary => SumKnown(x => x.SubTotalNw);

    /// <summary>任一有效行未知时毛重合计保持未知。</summary>
    public decimal? GrossWeightSummary => SumKnown(x => x.SubTotalGw);

    /// <summary>任一有效行未知时体积合计保持未知。</summary>
    public decimal? CbmSummary => SumKnown(x => x.SubTotalCbm);

    /// <summary>任一有效行未知时实际包装数合计保持未知。</summary>
    public decimal? CartonSummary => SumKnown(x => x.PackageCount);

    // 未知不能当成零；空列表的总和为零。
    private decimal? SumKnown(Func<PackingListLineModel, decimal?> selector)
    {
        var values = Items.Where(x => !x.IsDeleted).Select(selector).ToArray();
        if (values.Any(x => !x.HasValue)) return null;
        try { return values.Sum(x => x!.Value); }
        catch (OverflowException) { return null; }
    }
}

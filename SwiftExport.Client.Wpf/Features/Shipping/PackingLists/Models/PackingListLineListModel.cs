using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExportClient.Mvvm;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;

namespace SwiftExportClient.Features.Shipping.PackingLists.Models;

/// <summary>维护装箱单物理行及不混合单位的只读汇总，沿用基类删除池。</summary>
public sealed partial class PackingListLineListModel : BaseListModel<PackingListLineModel, PackingListLineDto>
{
    private bool _syncing;
    private string _shape = "";
    /// <summary>只对真实产品行建立分组视图，不混入假产品或空组行。</summary>
    public ICollectionView PackingView { get; }
    /// <summary>拼箱 UI 容器，允许暂存未保存的空组。</summary>
    public ObservableCollection<PackingListPackingGroupModel> PackingGroups { get; } = [];
    /// <summary>空组不能落库，需先填写或删除。</summary>
    public bool HasEmptyGroups => PackingGroups.Any(x => x.Members.Count == 0);
    /// <summary>拼箱共享字段不能被通用填充跨行覆盖。</summary>
    public bool CanUseGridOperations => !Items.Any(x => x.PackingGroupId.HasValue);
    /// <summary>订阅自身列表状态，覆盖编辑、增删、恢复和排序。</summary>
    public PackingListLineListModel() : base(_ => new PackingListLineModel())
    {
        PackingView = new ListCollectionView(Items);
        PackingView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PackingListLineModel.DisplayGroup)));
        ListStateChanged += (_, e) =>
        {
            if (_syncing) return;
            if (e.PropertyName == nameof(PackingListLineModel.Qty) && e.Line?.PackingGroupId is { } id)
            {
                _syncing = true;
                try { e.Line.PcsPerPackage = e.Line.Qty; ClearMetrics(id); }
                finally { _syncing = false; }
            }
            RefreshGroups();
        };
        ListStateChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(InvoiceNosSummary));
            OnPropertyChanged(nameof(QtyByUnit));
            OnPropertyChanged(nameof(QtySummary));
            OnPropertyChanged(nameof(NetWeightSummary));
            OnPropertyChanged(nameof(GrossWeightSummary));
            OnPropertyChanged(nameof(CbmSummary));
            OnPropertyChanged(nameof(CartonSummary));
            OnPropertyChanged(nameof(PackingSummary));
        };
    }

    /// <summary>整体加载不触发成员变动清空逻辑，移除旧的临时空组。</summary>
    public new void LoadFromDtos(IEnumerable<PackingListLineDto> dtos)
    {
        _syncing = true;
        try { PackingGroups.Clear(); base.LoadFromDtos(dtos); _shape = ""; }
        finally { _syncing = false; }
        RefreshGroups();
    }

    /// <summary>撤销恢复原始成员、顺序和包装字段；临时空组不保留。</summary>
    public new void RejectChanges()
    {
        _syncing = true;
        try { PackingGroups.Clear(); base.RejectChanges(); _shape = ""; }
        finally { _syncing = false; }
        RefreshGroups();
    }

    /// <summary>刷新结构时复用现有组对象，字段编辑不重建视图或打断 DataGrid 编辑。</summary>
    public void RefreshGroups()
    {
        if (_syncing) return;
        var shape = string.Join("|", Items.Select(x => $"{x.Uid}:{x.PackingGroupId}"));
        if (_shape == shape) return;
        _syncing = true;
        try
        {
            foreach (var group in Items.GroupBy(x => (Mixed: x.PackingGroupId.HasValue, Id: x.PackingGroupId ?? x.Uid)))
            {
                var rows = group.ToArray();
                var model = rows[0].PackingGroupId.HasValue
                    ? PackingGroups.FirstOrDefault(x => x.Id == group.Key.Id)
                    : rows[0].DisplayGroup;
                if (model is null || model.Id != group.Key.Id || model.IsMixed != rows[0].PackingGroupId.HasValue)
                {
                    model = new PackingListPackingGroupModel(group.Key.Id, rows[0].PackingGroupId.HasValue);
                    if (model.IsMixed) PackingGroups.Add(model);
                }
                model.BindMembers(rows);
                foreach (var row in rows) row.DisplayGroup = model;
            }
            foreach (var group in PackingGroups.Where(g => !Items.Any(x => x.PackingGroupId == g.Id)))
                group.BindMembers([]);
            _shape = shape;
            PackingView.Refresh();
            OnPropertyChanged(nameof(HasEmptyGroups));
            OnPropertyChanged(nameof(CanUseGridOperations));
        }
        finally { _syncing = false; }
    }

    /// <summary>成员变化使整箱重量体积失效，箱唛保留。</summary>
    public void ClearMetrics(Guid groupId)
    {
        foreach (var row in Items.Where(x => x.PackingGroupId == groupId).ToArray())
        { row.NwPerPackage = null; row.GwPerPackage = null; row.CbmPerPackage = null; }
        var group = PackingGroups.FirstOrDefault(x => x.Id == groupId);
        if (group != null) { group.NetWeight = null; group.GrossWeight = null; group.Volume = null; }
    }

    /// <summary>包装统计只计每条普通行和每个拼箱一次，不影响产品交付总量。</summary>
    private IEnumerable<PackingListLineModel> Packages => Items.Where(x => !x.IsDeleted)
        .GroupBy(x => (x.PackingListHeadId, x.PackingGroupId.HasValue, Id: x.PackingGroupId ?? x.Uid)).Select(x => x.First());

    /// <summary>显示已知合计及缺失提示，不把未知值伪装成完整总数。</summary>
    public string PackingSummary => $"箱数 {Known(x => x.PackageCount)}；净重 {Known(x => x.SubTotalNw)}；毛重 {Known(x => x.SubTotalGw)}；体积 {Known(x => x.SubTotalCbm)}";

    /// <summary>已知部分与未知提示并存，溢出不得显示伪造合计。</summary>
    private string Known(Func<PackingListLineModel, decimal?> selector)
    {
        var values = Packages.Select(selector).ToArray();
        try { return $"{values.Sum(x => x ?? 0):0.####}" + (values.Any(x => !x.HasValue) ? "（有未填写项）" : ""); }
        catch (OverflowException) { return "超出范围"; }
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
        var values = Packages.Select(selector).ToArray();
        if (values.Any(x => !x.HasValue)) return null;
        try { return values.Sum(x => x!.Value); }
        catch (OverflowException) { return null; }
    }
}

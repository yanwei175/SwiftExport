using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExportClient.Features.Inventory.Inbounds.Models;

namespace SwiftExportClient.Features.Inventory.Inbounds.ViewModels;

/// <summary>维护独立选源组件的候选行和勾选；数据由宿主提供，本阶段不调用 API 或打开 Dialog。</summary>
public partial class InboundSourcePickerViewModel : ObservableObject
{
    [ObservableProperty]
    public partial IReadOnlyList<InboundSourcePickerRowModel> Items { get; private set; }
        = Array.Empty<InboundSourcePickerRowModel>();

    /// <summary>替换当前候选快照；调用方应提供本次选择专用的行模型，不共享其他页面的勾选状态。</summary>
    public void LoadItems(IEnumerable<InboundSourcePickerRowModel> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        Items = items.ToArray();
    }

    /// <summary>读取当前勾选快照，供未来宿主在确认选择时接纳；不产生入库写入。</summary>
    public IReadOnlyList<InboundSourcePickerRowModel> GetCheckedItems() =>
        Items.Where(item => item.IsChecked).ToArray();

    /// <summary>仅切换当前候选集合的勾选，不改变来源数量或上游状态。</summary>
    [RelayCommand]
    private void ToggleAll()
    {
        var check = Items.Any(item => !item.IsChecked);
        foreach (var item in Items)
            item.IsChecked = check;
    }
}

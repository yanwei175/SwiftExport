using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExportClient.Features.Suppliers.Components.Model;

namespace SwiftExportClient.Features.Suppliers.Components.ViewModel
{
    /// <summary>供应商选择弹层的前端视图模型，仅负责本地候选展示与勾选状态。</summary>
    public sealed partial class SupplierPickerViewModel : ObservableObject
    {
        /// <summary>供应商搜索关键字，当前仅用于前端筛选输入占位。</summary>
        [ObservableProperty]
        public partial string Keyword { get; set; } = string.Empty;

        /// <summary>状态筛选的当前选择，默认展示全部。</summary>
        [ObservableProperty]
        public partial string ActiveFilter { get; set; } = "全部";

        /// <summary>状态筛选下拉选项，仅用于页面显示与交互占位。</summary>
        public IReadOnlyList<string> ActiveFilterOptions { get; } =
            ["全部", "启用", "停用"];

        /// <summary>供应商候选数据源。仅包含界面模型，不包含 API 查询职责。</summary>
        public ObservableCollection<SupplierPickerModel> Items { get; } = [];

        /// <summary>当前页内是否已全选。</summary>
        public bool IsAllChecked => Items.Count > 0 && Items.All(item => item.IsChecked);

        /// <summary>替换当前候选数据并刷新页面级勾选状态。</summary>
        public void LoadItems(IEnumerable<SupplierPickerModel> items)
        {
            ArgumentNullException.ThrowIfNull(items);
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }

            OnPropertyChanged(nameof(IsAllChecked));
        }

        /// <summary>返回当前勾选的供应商，用于外部页面提交。</summary>
        public IReadOnlyList<SupplierPickerModel> GetCheckedItems()
            => Items.Where(item => item.IsChecked).ToArray();

        /// <summary>清空页内勾选/反选状态。</summary>
        [RelayCommand]
        private void ToggleAll()
        {
            var target = !IsAllChecked;
            foreach (var item in Items)
            {
                item.IsChecked = target;
            }

            OnPropertyChanged(nameof(IsAllChecked));
        }

        /// <summary>单行勾选变更后回推全选状态，避免全选复用状态显示滞后。</summary>
        [RelayCommand]
        private void ToggleItem(SupplierPickerModel? item)
        {
            if (item is null)
            {
                return;
            }

            OnPropertyChanged(nameof(IsAllChecked));
        }
    }
}

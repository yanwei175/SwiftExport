using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.View.FW.Mvvm;
using System;
using System.Linq;

namespace SwiftExport.View.FW.SupplierViews.Models
{
    /// <summary>
    /// 供应商列表模型：基于 BaseListModel 统一管理集合、脏数据、全选反选、变更集。
    /// </summary>
    public class SupplierListModel : BaseListModel<SupplierModel>
    {
        /// <summary>
        /// 供应商总数（当前可见列表条数）。
        /// </summary>
        public int TotalSupplierCount => Items == null ? 0 : Items.Count;

        /// <summary>
        /// 供应商分页页码（当前小数据管理不分页，固定返回 1 兼容 UI）。
        /// </summary>
        public int PageSupplierIndex => 1;

        /// <summary>
        /// 供应商分页页大小（当前小数据管理不分页，返回当前条数）。
        /// </summary>
        public int PageSupplierSize => TotalSupplierCount <= 0 ? 1 : TotalSupplierCount;

        /// <summary>
        /// 本地搜索匹配规则。
        /// </summary>
        public bool MatchSearch(SupplierModel item, string searchText)
        {
            if (item == null) return false;
            if (string.IsNullOrWhiteSpace(searchText)) return true;

            return (item.SupplierCode?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                || (item.Name?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                || (item.DocRelateNo?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                || (item.ContactPerson?.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        /// <summary>
        /// 将 DTO 列表重载为行模型列表。
        /// </summary>
        public void ReloadFromDtoList(System.Collections.Generic.List<SupplierDto> dtos)
        {
            ReLoadRangeFromDtoList(dtos);
            RaisePropertyChanged(nameof(TotalSupplierCount));
            RaisePropertyChanged(nameof(PageSupplierIndex));
            RaisePropertyChanged(nameof(PageSupplierSize));
        }

        /// <summary>
        /// 新增一行并插入到列表首行。
        /// </summary>
        public SupplierModel AddNewOnTop()
        {
            var model = new SupplierModel();
            model.MarkAsAdded();
            AddModelToFirst(model);
            RaisePropertyChanged(nameof(TotalSupplierCount));
            RaisePropertyChanged(nameof(PageSupplierSize));
            return model;
        }

        /// <summary>
        /// 删除所有已勾选行：
        /// - 新增未落库行：物理移除
        /// - 已落库行：标记 Deleted 并从可见列表移除
        /// </summary>
        public int DeleteChecked()
        {
            var checkedItems = GetCheckedItems().ToList();
            foreach (var item in checkedItems)
            {
                Remove(item);
            }

            RaisePropertyChanged(nameof(TotalSupplierCount));
            RaisePropertyChanged(nameof(PageSupplierSize));
            return checkedItems.Count;
        }
    }
}

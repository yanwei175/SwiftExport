using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.ObjectModel;

namespace SwiftExport.View.FW.ProductViews.Models
{
    public class CategoryTreeModel : BaseLineModel
    {

        // ==========================================
        // 🚀 业务数据属性 (展开 Set 并在里面调用拦截器)
        // ==========================================

        private string _categoryName;
        public string CategoryName
        {
            get => _categoryName;
            set
            {
                if (SetProperty(ref _categoryName, value))
                    MarkAsModified();
            }
        }

        public Guid? ParentId { get; set; }

        private string _fakeName;
        public string FakeName
        {
            get => _fakeName;
            set
            {
                if (SetProperty(ref _fakeName, value))
                    MarkAsModified();
            }
        }

        private string _categoryCode;
        public string CategoryCode
        {
            get => _categoryCode;
            set
            {
                if (SetProperty(ref _categoryCode, value))
                    MarkAsModified();
            }
        }

        private int _sortNo;
        public int SortNo
        {
            get => _sortNo;
            set
            {
                if (SetProperty(ref _sortNo, value))
                    MarkAsModified();
            }
        }




        // ==========================================
        // 🚀 UI 树形专用
        // ==========================================

        // 存放子节点的集合 (这是无限级展开的核心！)
        public ObservableCollection<CategoryTreeModel> Children { get; set; } = new ObservableCollection<CategoryTreeModel>();

        // 控制树节点是否在 UI 上展开
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetUiProperty(ref _isExpanded, value);
        }

        // 控制树节点是否被选中
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetUiProperty(ref _isSelected, value);
        }
    }
}

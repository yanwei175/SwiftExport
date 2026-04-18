using SwiftExport.View.FW.Mvvm;
using System.Collections.ObjectModel;

namespace SwiftExport.View.FW.ProductViews.Models
{
    public class CategoryTreeModel : ViewModelBaseEx
    {
        public int Id { get; set; }

        private ModelState _state = ModelState.Unchanged;
        public ModelState State
        {
            get => _state;
            set => SetProperty(ref _state, value);
        }


        // ==========================================
        // ★ 核心魔法：状态拦截器
        // ==========================================
        private void MarkAsModified()
        {
            // 只有当它原本是 Unchanged (未修改) 时，才把它标记为 Modified。
            // 如果它本来就是 Added (新增) 或者 Deleted (删除)，千万别覆盖它的状态！
            if (State == ModelState.Unchanged)
            {
                State = ModelState.Modified;
            }
        }

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

        public int? ParentId { get; set; }

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
            set => SetProperty(ref _isExpanded, value);
        }

        // 控制树节点是否被选中
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{
    public abstract class BaseTreeNodeModel<T> : BaseLineModel
        where T : BaseTreeNodeModel<T>
    {
        // ================= 层级指针 =================
        public T Parent { get; set; }
        public ObservableCollection<T> Children { get; set; } = new ObservableCollection<T>();


        // ================= 树节点 UI 状态 =================
        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value) && value)
                {
                    // 核心需求1：点击(选中)时自动展开子节点
                    IsExpanded = true;
                    // 触发内部事件，由 ListModel 统一捕获
                    OnSelected();
                }
            }
        }
        private bool _isVisible = true;
        /// <summary>
        /// 用于搜索过滤：节点是否可见
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }
        private bool _isMatch;
        /// <summary>
        /// 用于搜索高亮：节点本身是否符合搜索关键字（用于前端改变颜色）
        /// </summary>
        public bool IsMatch
        {
            get => _isMatch;
            set => SetProperty(ref _isMatch, value);
        }
        // ================= 内部事件 =================
        public System.Action<T> NodeSelectedAction { get; set; }

        protected virtual void OnSelected()
        {
            NodeSelectedAction?.Invoke((T)this);
        }

        /// <summary>
        /// 核心需求2：展开当前节点及其所有父节点（用于搜索定位）
        /// </summary>
        public void 展开当前节点及其所有父节点()
        {
            IsExpanded = true;
            Parent?.展开当前节点及其所有父节点();
        }
        public List<int> 获取该节点下所有子节点Id()
        {
            var ids = new List<int> { this.Id };
            foreach (var child in Children)
            {
                ids.AddRange(child.获取该节点下所有子节点Id());
            }
            return ids;
        }

    }

}

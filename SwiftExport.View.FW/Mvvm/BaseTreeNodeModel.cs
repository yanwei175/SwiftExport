using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{
    /// <summary>
    /// 树形节点模型基类。
    /// 
    /// 设计原则：
    /// 1. 继承 BaseLineModel 以获得完整的 MVVM 状态管理（IBaseModel、IDataErrorInfo、状态机）
    /// 2. 添加树形专用属性：Parent、Children、IsExpanded、IsSelected、IsVisible、IsMatch
    /// 3. 不强制要求 ParentId 字段（由子类负责实现）
    /// 4. 提供树形操作辅助方法（展开、获取子节点等）
    /// 5. 支持事件驱动的节点选中通知（SelectedItemChanged）
    /// 
    /// 使用约束（依赖注入 + 泛型约束）：
    /// - BuildTree() 方法要求提供 idSelector 和 parentIdSelector 函数
    /// - Service 层负责验证 ParentId 字段的存在和有效性
    /// - UI 层使用 ITreeModelService<T> 来规范树形操作
    /// </summary>
    public abstract class BaseTreeNodeModel<T> : BaseLineModel
        where T : BaseTreeNodeModel<T>
    {
        // ================= 树形层级指针 =================
        /// <summary>
        /// 父级节点引用（构建树形结构时由 BuildTree 设置）
        /// </summary>
        public T Parent { get; set; }

        /// <summary>
        /// 子节点集合（可观察，支持 TreeView 绑定）
        /// </summary>
        public ObservableCollection<T> Children { get; set; } = new ObservableCollection<T>();


        // ================= 树形 UI 状态（不追踪为 Modified） =================
        private bool _isExpanded;
        /// <summary>
        /// 树形节点是否展开（UI 状态，不影响数据脏标记）
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private bool _isSelected;
        /// <summary>
        /// 树形节点是否被选中（UI 状态，不影响数据脏标记）
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value) && value)
                {
                    // 核心需求1：点击时自动展开子节点
                    IsExpanded = true;
                    // 触发选中事件，由 TreeList 统一捕获
                    OnSelected();
                }
            }
        }

        private bool _isVisible = true;
        /// <summary>
        /// 用于搜索过滤：节点是否可见（UI 状态，不影响数据脏标记）
        /// </summary>
        public bool IsVisible
        {
            get => _isVisible;
            set => SetProperty(ref _isVisible, value);
        }

        private bool _isMatch;
        /// <summary>
        /// 用于搜索高亮：节点本身是否符合搜索关键字（UI 状态，不影响数据脏标记）
        /// </summary>
        public bool IsMatch
        {
            get => _isMatch;
            set => SetProperty(ref _isMatch, value);
        }

        // ================= 事件与回调 =================
        /// <summary>
        /// 节点选中事件委托（由 TreeList 注入）
        /// </summary>
        public System.Action<T> NodeSelectedAction { get; set; }

        /// <summary>
        /// 当节点被选中时触发
        /// </summary>
        protected virtual void OnSelected()
        {
            NodeSelectedAction?.Invoke((T)this);
        }

        // ================= 树形操作辅助方法 =================

        /// <summary>
        /// 展开当前节点及所有父节点（用于搜索定位）
        /// </summary>
        public void ExpandNodeAndAncestors()
        {
            IsExpanded = true;
            Parent?.ExpandNodeAndAncestors();
        }

        /// <summary>
        /// 获取该节点及其下所有子节点的 ID 列表（用于批量操作）
        /// </summary>
        public List<Guid> GetDescendantIds()
        {
            var ids = new List<Guid> { this.Uid };
            foreach (var child in Children)
            {
                ids.AddRange(child.GetDescendantIds());
            }
            return ids;
        }

        /// <summary>
        /// 递归获取该节点的所有叶子节点（用于树形统计）
        /// </summary>
        public List<T> GetLeafNodes()
        {
            if (Children.Count == 0)
                return new List<T> { (T)this };

            var leaves = new List<T>();
            foreach (var child in Children)
            {
                leaves.AddRange(child.GetLeafNodes());
            }
            return leaves;
        }

        /// <summary>
        /// 获取该节点下所有子节点（不含自身）
        /// </summary>
        public List<T> GetAllDescendants()
        {
            var descendants = new List<T>();
            foreach (var child in Children)
            {
                descendants.Add(child);
                descendants.AddRange(child.GetAllDescendants());
            }
            return descendants;
        }

        /// <summary>
        /// 折叠该节点及所有子节点
        /// </summary>
        public void CollapseAll()
        {
            IsExpanded = false;
            foreach (var child in Children)
            {
                child.CollapseAll();
            }
        }

        /// <summary>
        /// 获取该节点的层级深度（根节点深度为 0）
        /// </summary>
        public int GetDepth()
        {
            if (Parent == null)
                return 0;
            return 1 + Parent.GetDepth();
        }
    }
}

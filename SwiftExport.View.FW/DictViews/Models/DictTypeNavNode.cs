using SwiftExport.View.FW.Mvvm;
using System;
using System.Collections.ObjectModel;

namespace SwiftExport.View.FW.DictViews.Models
{
    /// <summary>
    /// 字典类型导航树节点（只读展示用）
    /// 
    /// 职责：
    /// - 统一表示第一层分组节点（Group）和第二层及以上字典类型节点（DictType）
    /// - 仅用于 TreeView 导航展示，不承担任何编辑逻辑
    /// - 与后端 DictTypeGroupTreeDto / DictTypeTreeDto 解耦，由 ViewModel 负责转换
    /// </summary>
    public class DictTypeNavNode : ViewModelBaseEx
    {
        // ============= 基础展示属性 =============

        /// <summary>
        /// 显示名称（分组名 或 字典类型名）
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// 字典类型代码（仅当 IsGroup = false 时有值）
        /// </summary>
        public string DictCode { get; set; }

        /// <summary>
        /// 目标视图键（对应 DataDictManagerViewModel 路由表）
        /// 如：ProductColorView、ProductCategoryView 等
        /// 为空时右侧面板不加载任何内容
        /// </summary>
        public string TargetViewKey { get; set; }

        /// <summary>
        /// 是否为分组节点（Group 为 true，DictType 为 false）
        /// </summary>
        public bool IsGroup { get; set; }

        // ============= 树形状态属性 =============

        private bool _isExpanded;
        /// <summary>
        /// 是否展开（双向绑定，分组节点默认展开）
        /// </summary>
        public bool IsExpanded
        {
            get => _isExpanded;
            set => SetProperty(ref _isExpanded, value);
        }

        private bool _isSelected;
        /// <summary>
        /// 是否选中（双向绑定，选中时触发 NodeSelectedAction）
        /// </summary>
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (SetProperty(ref _isSelected, value) && value)
                {
                    NodeSelectedAction?.Invoke(this);
                }
            }
        }

        // ============= 子节点 =============

        /// <summary>
        /// 子节点集合（TreeView HierarchicalDataTemplate 绑定此属性）
        /// </summary>
        public ObservableCollection<DictTypeNavNode> Children { get; set; } = new ObservableCollection<DictTypeNavNode>();

        // ============= 事件回调 =============

        /// <summary>
        /// 节点被选中时的回调（由 ViewModel 注入）
        /// </summary>
        public Action<DictTypeNavNode> NodeSelectedAction { get; set; }
    }
}

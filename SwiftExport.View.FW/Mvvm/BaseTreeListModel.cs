using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{
    public abstract class BaseTreeListModel<T> : ViewModelBaseEx
        where T : BaseTreeNodeModel<T>, new()
    {
        /// <summary>
        /// 绑定给 TreeView.ItemsSource 的根节点集合
        /// </summary>
        public ObservableCollection<T> Roots { get; } = new ObservableCollection<T>();
        /// <summary>
        /// 扁平化的所有节点缓存，方便快速查找
        /// </summary>
        public List<T> AllNodes { get; } = new List<T>();

        /// <summary>
        /// 当某个节点被选中时触发（MainViewModel 订阅这个即可）
        /// </summary>
        public event Action<T> SelectedItemChanged;
        protected virtual void OnSelectedItemChanged(T node)
        {
            SelectedItemChanged?.Invoke(node);
        }



        /// <summary>
        /// 从扁平数据构建树形结构
        /// </summary>
        /// <param name="flatList">包含所有节点的扁平列表</param>
        /// <param name="idSelector">获取节点唯一标识的方法</param>
        /// <param name="parentIdSelector">获取节点父标识的方法（null 表示根节点）</param>
        public void BuildTree<TKey>(List<T> flatList, Func<T, TKey> idSelector, Func<T, TKey> parentIdSelector)
            where TKey : IEquatable<TKey>
        {
            ClearTree();

            Roots.Clear();
            AllNodes.Clear();
            AllNodes.AddRange(flatList);

            // 构建 ID → Node 字典（O(1) 查找）
            var nodeDict = flatList.ToDictionary(idSelector);

            foreach (var node in flatList)
            {
                // 1. 注入节点选中事件处理
                node.NodeSelectedAction = OnNodeSelected;

                // 2. 判断是否为根节点
                var parentId = parentIdSelector(node);
                bool isRoot = parentId == null || 
                              parentId.Equals(default(TKey)) || 
                              !nodeDict.ContainsKey(parentId);

                if (isRoot)
                {
                    Roots.Add(node);
                }
                else
                {
                    var parent = nodeDict[parentId];
                    node.Parent = parent;
                    parent.Children.Add(node);
                }
            }
        }
        private void OnNodeSelected(T node)
        {
            OnSelectedItemChanged(node);
        }

        // ================= 核心需求2：树形搜索与过滤 =================

        /// <summary>
        /// 搜索过滤树。
        /// 逻辑：如果不包含关键字且子节点都不包含，则隐藏；包含则高亮并展开父级。
        /// </summary>
        public void Filter(string keyword, Func<T, string, bool> isMatchPredicate)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                // 清空搜索时，恢复所有节点的默认状态
                foreach (var node in AllNodes)
                {
                    node.IsVisible = true;
                    node.IsMatch = false;
                }
                return;
            }

            foreach (var root in Roots)
            {
                FilterNode(root, keyword, isMatchPredicate);
            }
        }

        private bool FilterNode(T node, string keyword, Func<T, string, bool> isMatchPredicate)
        {
            // 1. 判断自身是否匹配
            bool selfMatch = isMatchPredicate(node, keyword);
            node.IsMatch = selfMatch;

            // 2. 递归判断子节点是否有匹配
            bool childMatch = false;
            foreach (var child in node.Children)
            {
                if (FilterNode(child, keyword, isMatchPredicate))
                {
                    childMatch = true;
                }
            }

            // 3. 如果自身匹配或子节点匹配，则本节点可见
            node.IsVisible = selfMatch || childMatch;

            // 4. 如果匹配成功，展开自身并向上追溯展开父节点
            if (node.IsVisible)
            {
                node.ExpandNodeAndAncestors();
            }

            return node.IsVisible;
        }

        // ================= 辅助方法 =================

        /// <summary>
        /// 展开或折叠所有节点
        /// </summary>
        public void ExpandAll(bool isExpanded = true)
        {
            foreach (var node in AllNodes)
            {
                node.IsExpanded = isExpanded;
            }
        }

        /// <summary>
        /// 清空树形结构（回收内存、解除事件引用）
        /// </summary>
        public virtual void ClearTree()
        {
            // 1. 显式解除委托引用，帮助 GC 飞速回收
            foreach (var node in AllNodes)
            {
                node.NodeSelectedAction = null;
                node.Parent = null;
                node.Children.Clear();
            }

            // 2. 清空集合
            Roots.Clear();
            AllNodes.Clear();
        }

        // ================= 树形查询辅助方法 =================

        /// <summary>
        /// 根据节点 ID 查找节点
        /// </summary>
        public T FindNodeById(Guid id)
        {
            return AllNodes.FirstOrDefault(n => n.Uid == id);
        }

        /// <summary>
        /// 获取所有可见的节点（用于导出等操作）
        /// </summary>
        public List<T> GetVisibleNodes()
        {
            return AllNodes.Where(n => n.IsVisible).ToList();
        }

        /// <summary>
        /// 获取所有已选中的节点
        /// </summary>
        public List<T> GetSelectedNodes()
        {
            return AllNodes.Where(n => n.IsSelected).ToList();
        }

        /// <summary>
        /// 获取所有根节点
        /// </summary>
        public List<T> GetRootNodes()
        {
            return Roots.ToList();
        }

        /// <summary>
        /// 获取所有叶子节点（无子节点的节点）
        /// </summary>
        public List<T> GetLeafNodes()
        {
            return AllNodes.Where(n => n.Children.Count == 0).ToList();
        }

        /// <summary>
        /// 递归清空所有节点的选中状态
        /// </summary>
        public void ClearSelection()
        {
            foreach (var node in AllNodes)
            {
                node.IsSelected = false;
            }
        }

        /// <summary>
        /// 递归全选所有节点
        /// </summary>
        public void SelectAll()
        {
            foreach (var node in AllNodes)
            {
                node.IsSelected = true;
            }
        }

        /// <summary>
        /// 获取某节点的所有子节点及自身（用于删除）
        /// </summary>
        public List<T> GetNodeAndDescendants(T node)
        {
            var result = new List<T> { node };
            result.AddRange(node.GetAllDescendants());
            return result;
        }






    }

}

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
        /// 通用的扁平数据转树算法
        /// </summary>
        /// <param name="flatList">包含所有节点的扁平列表</param>
        /// <param name="idSelector">获取主键的方法</param>
        /// <param name="parentIdSelector">获取父主键的方法</param>
        public void BuildTree<TKey>(List<T> flatList, Func<T, TKey> idSelector, Func<T, TKey> parentIdSelector)
        {
            ClearTree();

            Roots.Clear();
            AllNodes.Clear();
            AllNodes.AddRange(flatList);

            var dict = flatList.ToDictionary(idSelector);

            foreach (var node in flatList)
            {
                // 绑定节点选中事件
                node.NodeSelectedAction = OnNodeSelected;

                var parentId = parentIdSelector(node);
                // 判断是否为根节点 (假设父ID为空或0，或者在字典里找不到，即为根)
                if (parentId == null || parentId.Equals(default(TKey)) || !dict.ContainsKey(parentId))
                {
                    Roots.Add(node);
                }
                else
                {
                    var parent = dict[parentId];
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
                node.展开当前节点及其所有父节点();
            }

            return node.IsVisible;
        }

        // ================= 辅助方法 =================

        public void ExpandAll(bool isExpanded = true)
        {
            foreach (var node in AllNodes)
            {
                node.IsExpanded = isExpanded;
            }
        }


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






    }

}

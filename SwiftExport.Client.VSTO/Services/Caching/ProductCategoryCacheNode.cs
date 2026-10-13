using System;
using System.Collections.ObjectModel;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExport.Client.VSTO.Services.Caching
{
    /// <summary>
    /// 产品分类缓存节点，可直接作为 WPF TreeView 的节点数据源。
    /// 节点不包含编辑状态，业务页面不得通过它修改产品分类。
    /// </summary>
    public sealed class ProductCategoryCacheNode
    {
        private readonly CacheObservableCollection<ProductCategoryCacheNode> _children = new CacheObservableCollection<ProductCategoryCacheNode>();

        internal ProductCategoryCacheNode(ProductCategoryDto dto)
        {
            Uid = dto.Uid;
            ParentId = dto.ParentId;
            CategoryCode = dto.CategoryCode ?? string.Empty;
            CategoryName = dto.CategoryName ?? string.Empty;
            FakeName = dto.FakeName ?? string.Empty;
            SortNo = dto.SortNo;
            Children = new ReadOnlyObservableCollection<ProductCategoryCacheNode>(_children);
        }

        /// <summary>获取分类主键。</summary>
        public Guid Uid { get; }

        /// <summary>获取父分类主键；根节点为 null。</summary>
        public Guid? ParentId { get; }

        /// <summary>获取分类代码。</summary>
        public string CategoryCode { get; }

        /// <summary>获取分类名称。</summary>
        public string CategoryName { get; }

        /// <summary>获取分类显示名称。</summary>
        public string FakeName { get; }

        /// <summary>获取由当前分类及祖先 FakeName 拼接的完整显示路径。</summary>
        public string CategoryPath { get; private set; } = string.Empty;

        /// <summary>获取同级排序号。</summary>
        public int SortNo { get; }

        /// <summary>获取只读子分类集合，可绑定 HierarchicalDataTemplate.ItemsSource。</summary>
        public ReadOnlyObservableCollection<ProductCategoryCacheNode> Children { get; }

        internal void AddChild(ProductCategoryCacheNode child) => _children.Add(child);

        internal void SetCategoryPath(string categoryPath) => CategoryPath = categoryPath;
    }
}

using MateWpfThemes.FW.Controls;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.DI;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Controls
{
    public partial class CategoryTreeSelectorControl : UserControl
    {
        // 外部回调事件
        public event Action<ProductCategoryDto> OnCategorySelected;

        // 外部绑定属性
        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(nameof(SelectedItem), typeof(ProductCategoryDto), typeof(CategoryTreeSelectorControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedItemChanged));

        public ProductCategoryDto SelectedItem
        {
            get => (ProductCategoryDto)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        // 外部绑定搜索关键字：用于在 ResetFilter 时强制清空控件内搜索框。
        public static readonly DependencyProperty SearchTextProperty =
            DependencyProperty.Register(nameof(SearchText), typeof(string), typeof(CategoryTreeSelectorControl),
                new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public string SearchText
        {
            get => (string)GetValue(SearchTextProperty);
            set => SetValue(SearchTextProperty, value);
        }

        /// <summary>
        /// 外部绑定的 SelectedItem 变化回调。
        /// 这里只处理“被清空”场景：当外部把 SelectedItem 置为 null（如重置筛选）时，
        /// 同步清空当前树上的选中状态，避免 UI 仍然停留在旧选中项。
        /// </summary>
        private static void OnSelectedItemChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = d as CategoryTreeSelectorControl;
            if (control == null)
                return;

            // 仅处理清空场景；非空选择仍由用户点击树节点驱动。
            if (e.NewValue != null)
                return;

            foreach (var node in control.FilteredNodes)
            {
                control.SetSelectedRecursive(node, false);
            }

            // 外部执行重置时，同时清空分类搜索框绑定值，确保 UI 显示与筛选状态一致。
            control.SearchText = string.Empty;
        }

   

        // 内部数据源
        private readonly List<CategoryTreeModel> _allRawNodes = new List<CategoryTreeModel>();
        public ObservableCollection<CategoryTreeModel> FilteredNodes { get; } = new ObservableCollection<CategoryTreeModel>();

        public CategoryTreeSelectorControl()
        {
            InitializeComponent();
            TreeCategory.ItemsSource = FilteredNodes;
            this.Loaded += async (s, e) => await LoadInitialData();
        }

        #region 数据加载与处理

        private async Task LoadInitialData()
        {
            try
            {
                var api = IocProvider.Get<IProductCategoryApi>();
                var result = await api?.GetAllAsync();
                if (result?.Success == true && result.Data != null)
                {
                    var allDtos = result.Data.OrderBy(x => x.SortNo).ToList();

                    // 转换为 Model 结构
                    var byId = allDtos.ToDictionary(x => x.Id, x => new CategoryTreeModel
                    {
                        Id = x.Id,
                        ParentId = x.ParentId,
                        CategoryName = x.CategoryName,
                        CategoryCode = x.CategoryCode,
                        FakeName = x.FakeName,
                        SortNo = x.SortNo
                    });

                    _allRawNodes.Clear();
                    foreach (var dto in allDtos)
                    {
                        if (byId.TryGetValue(dto.Id, out var node))
                        {
                            if (dto.ParentId.HasValue && byId.ContainsKey(dto.ParentId.Value))
                                byId[dto.ParentId.Value].Children.Add(node);
                            else
                                _allRawNodes.Add(node);
                        }
                    }
                    RefreshTree();
                }
            }
            catch { /* 处理异常 */ }
        }

        private void RefreshTree()
        {
            FilteredNodes.Clear();

            // 1. 添加“全部分类”固定节点
            var allNode = new CategoryTreeModel { CategoryName = "全部分类", CategoryCode = "All", IsExpanded = true };
            allNode.PropertyChanged += Node_PropertyChanged;
            FilteredNodes.Add(allNode);

            // 2. 过滤并克隆节点
            string keyword = TxtSearch.Text?.Trim();
            var filtered = _allRawNodes
                .Select(x => CloneAndFilter(x, keyword))
                .Where(x => x != null);

            foreach (var node in filtered)
            {
                if (!string.IsNullOrEmpty(keyword)) SetExpansionRecursive(node, true);
                SubscribeEventsRecursive(node);
                FilteredNodes.Add(node);
            }
        }

        private CategoryTreeModel CloneAndFilter(CategoryTreeModel source, string keyword)
        {
            bool isMatch = string.IsNullOrEmpty(keyword) ||
                           (source.CategoryName?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);

            var children = source.Children
                .Select(c => CloneAndFilter(c, keyword))
                .Where(c => c != null).ToList();

            if (!isMatch && children.Count == 0) return null;

            var clone = new CategoryTreeModel
            {
                Id = source.Id,
                ParentId = source.ParentId,
                CategoryName = source.CategoryName,
                CategoryCode = source.CategoryCode,
                FakeName = source.FakeName,
                SortNo = source.SortNo
            };
            foreach (var child in children) clone.Children.Add(child);
            return clone;
        }

        private void SubscribeEventsRecursive(CategoryTreeModel node)
        {
            node.PropertyChanged += Node_PropertyChanged;
            foreach (var child in node.Children) SubscribeEventsRecursive(child);
        }

        private void Node_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CategoryTreeModel.IsSelected))
            {
                var node = sender as CategoryTreeModel;
                if (node != null && node.IsSelected)
                {
                    var dto = new ProductCategoryDto { Id = node.Id, CategoryName = node.CategoryName, ParentId = node.ParentId };
                    SelectedItem = dto;
                    OnCategorySelected?.Invoke(dto);
                }
            }
        }

        private void SetExpansionRecursive(CategoryTreeModel node, bool isExpanded)
        {
            if (node == null) return;
            node.IsExpanded = isExpanded;
            foreach (var child in node.Children) SetExpansionRecursive(child, isExpanded);
        }

        /// <summary>
        /// 递归设置节点选中状态。
        /// 用于外部清空筛选条件时，统一把 TreeView 的可视选中态清掉。
        /// </summary>
        private void SetSelectedRecursive(CategoryTreeModel node, bool isSelected)
        {
            if (node == null) return;
            node.IsSelected = isSelected;
            foreach (var child in node.Children) SetSelectedRecursive(child, isSelected);
        }

        #endregion

        #region UI 事件驱动逻辑

        private bool IsExpaned { get; set; }
        private void BtnExpandAll_Click(object sender, RoutedEventArgs e)
        {
            if (!IsExpaned)
            {
                foreach (var node in FilteredNodes) SetExpansionRecursive(node, true);
                IsExpaned = true;
            }
            else
            {
                foreach (var node in FilteredNodes) SetExpansionRecursive(node, false);
                IsExpaned = false;
            }
        }

        private void BtnCollapseAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var node in FilteredNodes) SetExpansionRecursive(node, false);
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            RefreshTree();
        }

        #endregion
    }
}
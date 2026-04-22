using GalaSoft.MvvmLight.Ioc;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.DTOs.Products;
using SwiftExport.View.FW.ProductViews.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using GalaSoft.MvvmLight.Command;
using System.ComponentModel;

namespace SwiftExport.View.FW.ProductViews.Controls
{
    /// <summary>
    /// 自治分类树选择控件
    /// </summary>
    public partial class CategoryTreeSelectorControl : UserControl
    {
        //对外暴露的双向绑定属性 SelectedItem (根据API定义为 CategorySelectionModel)
        public event Action<CategorySelectionModel> OnCategorySelected;

        public static readonly DependencyProperty SelectedItemProperty =
            DependencyProperty.Register(
                nameof(SelectedItem),
                typeof(CategorySelectionModel),
                typeof(CategoryTreeSelectorControl),
                new FrameworkPropertyMetadata(
                    null,
                    FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

        public CategorySelectionModel SelectedItem
        {
            get => (CategorySelectionModel)GetValue(SelectedItemProperty);
            set => SetValue(SelectedItemProperty, value);
        }

        public static readonly DependencyProperty SearchKeywordProperty =
            DependencyProperty.Register(
                nameof(SearchKeyword),
                typeof(string),
                typeof(CategoryTreeSelectorControl),
                new PropertyMetadata(string.Empty, OnSearchKeywordChanged));

        public string SearchKeyword
        {
            get => (string)GetValue(SearchKeywordProperty);
            set => SetValue(SearchKeywordProperty, value);
        }

        public ObservableCollection<CategoryTreeModel> CategoryTreeFilterNodes { get; } = new ObservableCollection<CategoryTreeModel>();

        private readonly List<CategoryTreeModel> _allCategoryTreeTemplates = new List<CategoryTreeModel>();
        private bool _isLoaded;
        public RelayCommand ClearOtherSearchFiltersCommand { get; private set; }
        public RelayCommand ExpandAllCategoryCommand { get; private set; }
        public RelayCommand CollapseAllCategoryCommand { get; private set; }

        public CategoryTreeSelectorControl()
        {
            InitializeComponent();
            ExpandAllCategoryCommand = new RelayCommand(() => ToggleCategoryTreeExpansion(true));
            CollapseAllCategoryCommand = new RelayCommand(() => ToggleCategoryTreeExpansion(false));
            ClearOtherSearchFiltersCommand = new RelayCommand(() => SearchKeyword = string.Empty);
            Loaded += OnLoadedAsync;
        }

        private async void OnLoadedAsync(object sender, RoutedEventArgs e)
        {
            if (_isLoaded) return;
            _isLoaded = true;
            _= LoadDataAsync();
        }

        private async Task LoadDataAsync()
        {
            try
            {
                var api = SimpleIoc.Default.GetInstance<IProductCategoryApi>();
                if (api == null) return;

                var result = await api.GetAllAsync();
                if (result != null && result.Success && result.Data != null)
                {
                    _allCategoryTreeTemplates.Clear();

                    var allDtos = result.Data.OrderBy(x => x.SortNo).ToList();
                    var byId = allDtos.ToDictionary(x => x.Id, x => new CategoryTreeModel
                    {
                        Id = x.Id,
                        ParentId = x.ParentId,
                        CategoryName = x.CategoryName,
                        CategoryCode = x.CategoryCode,
                        FakeName = x.FakeName,
                        SortNo = x.SortNo
                    });

                    foreach (var dto in allDtos)
                    {
                        CategoryTreeModel node;
                        if (!byId.TryGetValue(dto.Id, out node)) continue;

                        if (dto.ParentId.HasValue && byId.ContainsKey(dto.ParentId.Value))
                        {
                            byId[dto.ParentId.Value].Children.Add(node);
                        }
                        else
                        {
                            _allCategoryTreeTemplates.Add(node);
                        }
                    }

                    BuildCategoryTree();
                }
            }
            catch
            {
                // Ignore error
            }
        }

        private static void OnSearchKeywordChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is CategoryTreeSelectorControl control)
            {
                control.BuildCategoryTree();
            }
        }

        private void BuildCategoryTree()
        {
            CategoryTreeFilterNodes.Clear();

            var allNode = new CategoryTreeModel { Id = 1, CategoryName = "全部分类", IsExpanded = true };
            SubscribeCategoryNode(allNode);
            CategoryTreeFilterNodes.Add(allNode);

            var clonedRoots = _allCategoryTreeTemplates
                .Select(CloneCategoryNode)
                .Where(x => IsNodeVisibleByKeyword(x, SearchKeyword))
                .ToList();

            bool forceExpand = !string.IsNullOrWhiteSpace(SearchKeyword);

            foreach (var node in clonedRoots)
            {
                node.IsExpanded = forceExpand || true;
                if (forceExpand) SetNodeExpansion(node, true);

                SubscribeCategoryNode(node);
                CategoryTreeFilterNodes.Add(node);
            }
        }

        private static CategoryTreeModel CloneCategoryNode(CategoryTreeModel source)
        {
            var clone = new CategoryTreeModel
            {
                Id = source.Id,
                ParentId = source.ParentId,
                CategoryName = source.CategoryName,
                CategoryCode = source.CategoryCode,
                FakeName = source.FakeName,
                SortNo = source.SortNo
            };
            foreach (var child in source.Children) clone.Children.Add(CloneCategoryNode(child));
            return clone;
        }

        private static bool IsNodeVisibleByKeyword(CategoryTreeModel node, string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return true;
            if (node.CategoryName?.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return node.Children.Any(child => IsNodeVisibleByKeyword(child, keyword));
        }

        private void ToggleCategoryTreeExpansion(bool isExpanded)
        {
            foreach (var node in CategoryTreeFilterNodes)
            {
                SetNodeExpansion(node, isExpanded);
            }
        }

        private void SetNodeExpansion(CategoryTreeModel node, bool isExpanded)
        {
            if (node == null) return;
            node.IsExpanded = isExpanded;
            if (node.Children != null)
            {
                foreach (var child in node.Children)
                {
                    SetNodeExpansion(child, isExpanded);
                }
            }
        }

        private void SubscribeCategoryNode(CategoryTreeModel node)
        {
            node.PropertyChanged += CategoryNode_PropertyChanged;
            foreach (var child in node.Children) SubscribeCategoryNode(child);
        }

        private void CategoryNode_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(CategoryTreeModel.IsSelected)) return;

            var node = sender as CategoryTreeModel;
            if (node == null || !node.IsSelected) return;

            var selection = new CategorySelectionModel
            {
                CurrentCategory = new ProductCategoryDto { Id = node.Id, CategoryName = node.CategoryName, ParentId = node.ParentId }
            };

            var flatList = new List<string>();
            CollectCategoryNames(node, flatList);
            selection.IncludedChildren = flatList.Select(x => new ProductCategoryDto { CategoryName = x }).ToList();

            SelectedItem = selection;
            OnCategorySelected?.Invoke(selection);
        }

         private void CollectCategoryNames(CategoryTreeModel node, List<string> list)
        {
            if (node == null) return;
            if (!list.Contains(node.CategoryName)) list.Add(node.CategoryName);
            if (node.Children == null) return;
            foreach (var child in node.Children)
            {
                CollectCategoryNames(child, list);
            }
        }
    }
}

using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Services.Caching;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.Components;

/// <summary>
/// 为分类筛选控件保存独立选择状态，不修改共享的只读分类缓存节点。
/// </summary>
internal sealed class ProductCategoryFilterNode : ObservableObject
{
    private bool _isExpanded = true;
    private bool _isSelfSelected;

    private ProductCategoryFilterNode(
        ProductCategoryFilterNodeKind kind,
        string displayName,
        ProductCategoryCacheNode? source,
        ProductCategoryFilterNode? parent)
    {
        Kind = kind;
        DisplayName = displayName;
        Source = source;
        Parent = parent;
    }

    /// <summary>创建“全部分类”特殊节点。</summary>
    public static ProductCategoryFilterNode CreateAll() =>
        new(ProductCategoryFilterNodeKind.All, "全部分类", null, null);

    /// <summary>创建“未分类”特殊节点。</summary>
    public static ProductCategoryFilterNode CreateUncategorized() =>
        new(ProductCategoryFilterNodeKind.Uncategorized, "未分类", null, null);

    /// <summary>从只读缓存节点递归创建筛选投影。</summary>
    public static ProductCategoryFilterNode FromCategory(
        ProductCategoryCacheNode source,
        ProductCategoryFilterNode? parent = null)
    {
        var node = new ProductCategoryFilterNode(
            ProductCategoryFilterNodeKind.Category,
            source.FakeName,
            source,
            parent);
        foreach (var child in source.Children)
        {
            node.Children.Add(FromCategory(child, node));
        }

        return node;
    }

    /// <summary>获取节点类型。</summary>
    public ProductCategoryFilterNodeKind Kind { get; }

    /// <summary>获取节点显示文本。</summary>
    public string DisplayName { get; }

    /// <summary>获取原始只读分类节点；特殊节点为空。</summary>
    public ProductCategoryCacheNode? Source { get; }

    /// <summary>获取父筛选节点。</summary>
    public ProductCategoryFilterNode? Parent { get; }

    /// <summary>获取子筛选节点。</summary>
    public ObservableCollection<ProductCategoryFilterNode> Children { get; } = [];

    /// <summary>获取或设置节点是否展开。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>
    /// 获取子树汇总选择状态；空值表示节点自身或后代只有部分被纳入。
    /// </summary>
    public bool? IsChecked
    {
        get
        {
            if (Children.Count == 0)
            {
                return _isSelfSelected;
            }

            var allChildrenSelected = Children.All(child => child.IsChecked == true);
            if (_isSelfSelected && allChildrenSelected)
            {
                return true;
            }

            var noChildrenSelected = Children.All(child => child.IsChecked == false);
            return !_isSelfSelected && noChildrenSelected ? false : null;
        }
    }

    /// <summary>获取节点自身是否被纳入筛选。</summary>
    public bool IsSelfSelected => _isSelfSelected;

    /// <summary>设置节点自身选择状态，并通知三态显示刷新。</summary>
    public void SetSelfSelected(bool value)
    {
        if (_isSelfSelected == value)
        {
            return;
        }

        _isSelfSelected = value;
        OnPropertyChanged(nameof(IsSelfSelected));
        OnPropertyChanged(nameof(IsChecked));
    }

    /// <summary>将同一选择状态应用到节点自身及全部后代。</summary>
    public void SetSubtreeSelected(bool value)
    {
        SetSelfSelected(value);
        foreach (var child in Children)
        {
            child.SetSubtreeSelected(value);
        }
    }

    /// <summary>通知当前节点重新计算三态显示。</summary>
    public void RefreshCheckState() => OnPropertyChanged(nameof(IsChecked));
}

/// <summary>区分真实分类与两个筛选特殊节点。</summary>
internal enum ProductCategoryFilterNodeKind
{
    All,
    Uncategorized,
    Category,
}

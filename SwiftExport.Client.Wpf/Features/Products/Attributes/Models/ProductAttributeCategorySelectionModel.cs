using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SwiftExportClient.Features.Products.Attributes.Models;

/// <summary>单组直接适用分类的私有草稿；多选控件确定后更新集合，继承分类不在此勾选。</summary>
public sealed partial class ProductAttributeCategorySelectionModel : ObservableObject, IDisposable
{
    private readonly HashSet<Guid> _baselineIds;

    /// <summary>以服务器直接关联建立基线，候选对象不承载勾选标记。</summary>
    public ProductAttributeCategorySelectionModel(IEnumerable<ProductAttributeCategoryCandidate> selected)
    {
        SelectedCategories = new(selected);
        _baselineIds = SelectedCategories.Select(item => item.Uid).ToHashSet();
        SelectedCategories.CollectionChanged += OnSelectionChanged;
    }

    /// <summary>当前组已确认的分类集合，仅属于本页面，关闭弹出层不会更新它。</summary>
    public ObservableCollection<ProductAttributeCategoryCandidate> SelectedCategories { get; }
    /// <summary>集合身份与服务器直接关联是否存在差异，不受勾选顺序影响。</summary>
    public bool HasChanges => !_baselineIds.SetEquals(SelectedCategories.Select(item => item.Uid));
    /// <summary>当前组直接关联的路径列表，继承范围由产品详情和缓存解析。</summary>
    public string SelectedPaths => SelectedCategories.Count == 0 ? "尚未直接关联分类。" :
        string.Join("；", SelectedCategories.Select(item => item.CategoryPath));

    /// <summary>通知宿主真实关系草稿变化，不修改标签组字段或共享资料。</summary>
    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(SelectedPaths));
    }

    /// <summary>释放私有集合订阅，避免已经替换的关系工作区继续通知页面。</summary>
    public void Dispose() => SelectedCategories.CollectionChanged -= OnSelectionChanged;
}

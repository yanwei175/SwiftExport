using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace SwiftExportClient.Features.Products.Attributes.Models;

/// <summary>单组直接适用属性模板的私有草稿；多选控件确定后更新集合，各属性模板直接共享标签组。</summary>
public sealed partial class ProductAttributeProfileSelectionModel : ObservableObject, IDisposable
{
    private readonly HashSet<Guid> _baselineIds;

    /// <summary>以服务器直接关联建立基线，候选对象不承载勾选标记。</summary>
    public ProductAttributeProfileSelectionModel(IEnumerable<ProductAttributeProfileCandidate> selected)
    {
        SelectedProfiles = new(selected);
        _baselineIds = SelectedProfiles.Select(item => item.Uid).ToHashSet();
        SelectedProfiles.CollectionChanged += OnSelectionChanged;
    }

    /// <summary>当前组已确认的属性模板集合，仅属于本页面，关闭弹出层不会更新它。</summary>
    public ObservableCollection<ProductAttributeProfileCandidate> SelectedProfiles { get; }
    /// <summary>集合身份与服务器直接关联是否存在差异，不受勾选顺序影响。</summary>
    public bool HasChanges => !_baselineIds.SetEquals(SelectedProfiles.Select(item => item.Uid));
    /// <summary>当前组直接关联的模板名称列表，适用范围由产品详情和缓存解析。</summary>
    public string SelectedPaths => SelectedProfiles.Count == 0 ? "尚未直接关联属性模板。" :
        string.Join("；", SelectedProfiles.Select(item => item.ProfileName));

    /// <summary>通知宿主真实关系草稿变化，不修改标签组字段或共享资料。</summary>
    private void OnSelectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        OnPropertyChanged(nameof(HasChanges));
        OnPropertyChanged(nameof(SelectedPaths));
    }

    /// <summary>释放私有集合订阅，避免已经替换的关系工作区继续通知页面。</summary>
    public void Dispose() => SelectedProfiles.CollectionChanged -= OnSelectionChanged;
}

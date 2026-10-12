using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>主型号详情的标签选择工作区，按身份跟踪草稿和基线，缓存更新不接纳产品变更。</summary>
public sealed partial class ProductMasterTagSelectionModel : ObservableObject, IDisposable
{
    private HashSet<Guid> _selectedIds = [];
    private HashSet<Guid> _baselineIds = [];
    private readonly Dictionary<Guid, string> _knownNames = [];
    private IReadOnlyList<ProductAttributeGroupDto> _definitions = [];
    private int _openPopupCount;
    private bool _pendingRefresh;
    private bool _disposed;

    /// <summary>工作区的最终选择、可用性或分类变化后通知父详情重新计算保存能力。</summary>
    public event EventHandler? StateChanged;
    /// <summary>获取按当前具体分类生成的组编辑器。</summary>
    public ObservableCollection<ProductAttributeGroupSelectionModel> Groups { get; } = [];
    /// <summary>获取未匹配到当前定义的选择，保留身份并允许显式移除。</summary>
    public ObservableCollection<ProductMasterMissingTagModel> MissingSelections { get; } = [];
    /// <summary>获取已保存选择的分类。</summary>
    public Guid BaselineCategoryId { get; private set; }
    /// <summary>获取当前编辑分类。</summary>
    public Guid CategoryId { get; private set; }
    /// <summary>获取身份集合是否存在真实变化，忽略排列及候选实例变化。</summary>
    public bool IsDirty => !_selectedIds.SetEquals(_baselineIds);
    /// <summary>获取当前是否存在已确认选择。</summary>
    public bool HasSelections => _selectedIds.Count > 0;
    /// <summary>获取定义是否已加载，包含成功的空快照。</summary>
    public bool AreDefinitionsLoaded { get; private set; }
    /// <summary>获取选择编辑器是否拥有有效定义。</summary>
    public bool CanEdit => AreDefinitionsLoaded && CategoryId != Guid.Empty;
    /// <summary>获取是否仍有未提交弹出层草稿。</summary>
    public bool IsPopupOpen => _openPopupCount > 0;
    /// <summary>获取标签区状态及可重试提示。</summary>
    [ObservableProperty] public partial string StatusText { get; private set; } = "标签定义尚未加载，请刷新标签。";

    /// <summary>接纳服务端聚合选择为基线；显示资料只用于无法匹配时的提示。</summary>
    public void Load(Guid categoryId, IEnumerable<Guid>? selectedIds, IEnumerable<ProductMasterAttributeSelectionDto>? display)
    {
        BaselineCategoryId = CategoryId = categoryId;
        _selectedIds = (selectedIds ?? []).ToHashSet();
        _baselineIds = _selectedIds.ToHashSet();
        _knownNames.Clear();
        foreach (var item in display ?? [])
            _knownNames[item.OptionId] = string.IsNullOrWhiteSpace(item.OptionName)
                ? $"未知标签 {item.OptionId}" : $"{item.GroupName}：{item.OptionName}";
        RebuildGroups();
    }

    /// <summary>在父详情已取得用户确认后更换分类并清除本地旧标签，不调用 API。</summary>
    public void ChangeCategory(Guid categoryId)
    {
        CategoryId = categoryId;
        _selectedIds.Clear();
        RebuildGroups();
    }

    /// <summary>撤销选择并恢复原分类；头部回滚由父详情统一完成。</summary>
    public void RejectChanges()
    {
        CategoryId = BaselineCategoryId;
        _selectedIds = _baselineIds.ToHashSet();
        RebuildGroups();
    }

    /// <summary>提交只读缓存快照；弹出层有临时草稿时延迟重建候选，保留已选身份。</summary>
    public void RefreshDefinitions(IEnumerable<ProductAttributeGroupDto> definitions, bool loaded)
    {
        _definitions = definitions.ToArray();
        AreDefinitionsLoaded = loaded;
        if (IsPopupOpen)
        {
            _pendingRefresh = true;
            // 会话清理必须立即禁用编辑；只把候选对象替换延迟到弹出层关闭。
            StatusText = loaded ? "标签定义已更新，关闭选择弹出层后更新候选。"
                : "标签定义尚未加载，请刷新标签；已保存选择保持不变。";
            OnPropertyChanged(nameof(AreDefinitionsLoaded));
            OnPropertyChanged(nameof(CanEdit));
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        RebuildGroups();
    }

    /// <summary>标记公共多选弹出层打开，仅作为视觉桥接状态。</summary>
    public void BeginPopupEdit() => _openPopupCount++;

    /// <summary>弹出层关闭后接纳等待中的定义更新，确定和取消均保留各自选择语义。</summary>
    public void EndPopupEdit()
    {
        if (_openPopupCount > 0) _openPopupCount--;
        if (!IsPopupOpen && _pendingRefresh)
        {
            _pendingRefresh = false;
            RebuildGroups();
        }
    }

    /// <summary>定义可用时返回完整最终集合；未加载保留旧协议的 null，绝不冒充清空。</summary>
    public List<Guid>? GetSelectedOptionIds() => AreDefinitionsLoaded ? _selectedIds.OrderBy(id => id).ToList() : null;

    /// <summary>阻断不完整定义下的标签修改、跨分类旧标签保持以及单选冲突；后端仍最终校验。</summary>
    public string? ValidateSelection(Guid finalCategoryId)
    {
        if (IsPopupOpen) return "请先确定或关闭标签选择弹出层。";
        if (!AreDefinitionsLoaded && (IsDirty || (finalCategoryId != BaselineCategoryId && _baselineIds.Count > 0)))
            return "标签定义尚未加载，标签修改已保留。请刷新标签后再保存。";
        if (Groups.Any(group => group.HasSingleSelectionConflict)) return "单选标签组存在多个值，请重新选择或清除。";
        if (MissingSelections.Count > 0 && AreDefinitionsLoaded) return "已选标签在当前分类定义中失效，请刷新核对或明确移除后保存。";
        return null;
    }

    /// <summary>用户明确移除失效身份；缓存刷新本身不能执行此操作。</summary>
    [RelayCommand]
    private void RemoveMissingSelection(ProductMasterMissingTagModel? selection)
    {
        if (_disposed || !AreDefinitionsLoaded || selection is null) return;
        _selectedIds.Remove(selection.OptionId);
        RebuildGroups();
    }

    /// <summary>根据新定义重建私有编辑器，按 Uid 匹配并保留不可用选择的可识别提示。</summary>
    private void RebuildGroups()
    {
        if (_disposed) return;
        foreach (var group in Groups) group.SelectionCommitted -= OnGroupCommitted;
        Groups.Clear();
        MissingSelections.Clear();
        var availableIds = new HashSet<Guid>();
        if (AreDefinitionsLoaded)
        {
            foreach (var definition in _definitions.Where(item => item.CategoryId == CategoryId)
                         .OrderBy(item => item.SortOrder).ThenBy(item => item.Uid))
            {
                foreach (var option in definition.Options ?? [])
                {
                    availableIds.Add(option.Uid);
                    _knownNames[option.Uid] = $"{definition.GroupName}：{option.OptionName}";
                }
                var group = new ProductAttributeGroupSelectionModel(definition, _selectedIds);
                group.SelectionCommitted += OnGroupCommitted;
                Groups.Add(group);
            }
            foreach (var id in _selectedIds.Except(availableIds).OrderBy(id => id))
                MissingSelections.Add(new(id, _knownNames.GetValueOrDefault(id, $"未知标签 {id}")));
        }
        StatusText = !AreDefinitionsLoaded ? "标签定义尚未加载，请刷新标签；已保存选择保持不变。"
            : CategoryId == Guid.Empty ? "请先选择产品分类。"
            : MissingSelections.Count > 0 ? "部分已选标签已失效，身份已保留，请核对后明确移除。"
            : Groups.Count == 0 ? "当前分类没有标签定义。" : "标签选择随整页保存。";
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasSelections));
        OnPropertyChanged(nameof(AreDefinitionsLoaded));
        OnPropertyChanged(nameof(CanEdit));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnGroupCommitted(object? sender, EventArgs e)
    {
        if (_disposed || !AreDefinitionsLoaded || sender is not ProductAttributeGroupSelectionModel group) return;
        // 仅替换当前候选范围，未知或失效身份必须经明确移除操作处理。
        _selectedIds.ExceptWith(group.Options.Select(option => option.Uid));
        _selectedIds.UnionWith(group.SelectedItems.Select(option => option.Uid));
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasSelections));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>解除组事件，保证详情释放后不能继续发布选择状态。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var group in Groups) group.SelectionCommitted -= OnGroupCommitted;
    }
}

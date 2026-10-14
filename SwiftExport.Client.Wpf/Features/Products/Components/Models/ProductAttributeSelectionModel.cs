using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExportClient.Services.Caching;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.Components.Models;

/// <summary>产品对象详情共用的标签选择工作区，按选项身份跟踪私有草稿；所属对象、模板来源及保存事务由页面负责。</summary>
public sealed partial class ProductAttributeSelectionModel : ObservableObject, IDisposable
{
    private HashSet<Guid> _selectedIds = [];
    private HashSet<Guid> _baselineIds = [];
    private readonly Dictionary<Guid, string> _knownNames = [];
    private IReadOnlyList<ProductAttributeGroupDto> _definitions = [];
    private string _resolutionMessage = "标签定义尚未加载，请刷新标签。";
    private int _openPopupCount;
    private bool _pendingRefresh;
    private bool _disposed;

    /// <summary>工作区的最终选择、可用性或属性模板变化后通知父详情重新计算保存能力。</summary>
    public event EventHandler? StateChanged;
    /// <summary>获取按当前属性模板直接关联结果生成的组编辑器，共享组只显示一次。</summary>
    public ObservableCollection<ProductAttributeGroupSelectionModel> Groups { get; } = [];
    /// <summary>获取未匹配到当前定义的选择，保留身份并允许显式移除。</summary>
    public ObservableCollection<ProductAttributeMissingSelectionModel> MissingSelections { get; } = [];
    /// <summary>获取已保存选择的属性模板。</summary>
    public Guid BaselineProfileId { get; private set; }
    /// <summary>获取当前编辑属性模板。</summary>
    public Guid ProfileId { get; private set; }
    /// <summary>获取身份集合是否存在真实变化，忽略排列及候选实例变化。</summary>
    public bool IsDirty => !_selectedIds.SetEquals(_baselineIds);
    /// <summary>获取当前是否存在已确认选择。</summary>
    public bool HasSelections => _selectedIds.Count > 0;
    /// <summary>获取当前属性模板的字典与定义是否已完整解析，包含正常空候选。</summary>
    public bool AreDefinitionsLoaded { get; private set; }
    /// <summary>获取选择编辑器是否拥有有效定义。</summary>
    public bool CanEdit => AreDefinitionsLoaded && ProfileId != Guid.Empty;
    /// <summary>获取是否仍有未提交弹出层草稿。</summary>
    public bool IsPopupOpen => _openPopupCount > 0;
    /// <summary>获取标签区状态及可重试提示。</summary>
    [ObservableProperty] public partial string StatusText { get; private set; } = "标签定义尚未加载，请刷新标签。";

    /// <summary>接纳服务端聚合选择为基线；显示资料只用于无法匹配时的提示。</summary>
    public void Load(Guid profileId, IEnumerable<Guid>? selectedIds, IEnumerable<ProductAttributeSelectionDto>? display)
    {
        BaselineProfileId = ProfileId = profileId;
        _selectedIds = (selectedIds ?? []).ToHashSet();
        _baselineIds = _selectedIds.ToHashSet();
        _knownNames.Clear();
        foreach (var item in display ?? [])
            _knownNames[item.OptionId] = string.IsNullOrWhiteSpace(item.OptionName)
                ? $"未知标签 {item.OptionId}" : $"{item.GroupName}：{item.OptionName}";
        RebuildGroups();
    }

    /// <summary>获取目标属性模板不再适用的已选身份，解析失败时不能推断任何选择应该清除。</summary>
    public IReadOnlyList<Guid> GetInapplicableOptionIds(ProductAttributeResolution resolution)
    {
        if (!resolution.Success) throw new InvalidOperationException("标签适用范围解析失败，不能决定清除哪些选择。");
        var availableIds = resolution.Groups.SelectMany(group => group.Options ?? []).Select(option => option.Uid).ToHashSet();
        return _selectedIds.Except(availableIds).OrderBy(id => id).ToArray();
    }

    /// <summary>父详情确认后更换属性模板，仅移除目标范围不再适用的身份，共同标签和原基线保留。</summary>
    public void ChangeProfile(Guid profileId, ProductAttributeResolution resolution)
    {
        ProfileId = profileId;
        if (resolution.Success) _selectedIds.ExceptWith(GetInapplicableOptionIds(resolution));
        RefreshDefinitions(resolution);
    }

    /// <summary>撤销选择并恢复原属性模板；头部回滚由父详情统一完成。</summary>
    public void RejectChanges()
    {
        ProfileId = BaselineProfileId;
        _selectedIds = _baselineIds.ToHashSet();
        RebuildGroups();
    }

    /// <summary>提交只读缓存快照；弹出层有临时草稿时延迟重建候选，保留已选身份。</summary>
    public void RefreshDefinitions(ProductAttributeResolution resolution)
    {
        _definitions = resolution.Groups;
        _resolutionMessage = resolution.Message;
        AreDefinitionsLoaded = resolution.Success;
        if (IsPopupOpen)
        {
            _pendingRefresh = true;
            // 会话清理必须立即禁用编辑；只把候选对象替换延迟到弹出层关闭。
            StatusText = resolution.Success ? "标签定义已更新，关闭选择弹出层后更新候选。"
                : $"{_resolutionMessage} 已保存选择保持不变，请刷新标签。";
            OnPropertyChanged(nameof(AreDefinitionsLoaded));
            OnPropertyChanged(nameof(CanEdit));
            StateChanged?.Invoke(this, EventArgs.Empty);
            return;
        }
        RebuildGroups();
    }

    /// <summary>刷新下游对象只读来源模板，保留所有选值和回滚基线；失效项只能经用户明确移除。</summary>
    public void RefreshSourceProfile(Guid profileId, ProductAttributeResolution resolution)
    {
        ProfileId = profileId;
        RefreshDefinitions(resolution);
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

    /// <summary>当前属性模板完整解析时返回最终集合；失败保留 null 协议，绝不冒充清空。</summary>
    public List<Guid>? GetSelectedOptionIds() => AreDefinitionsLoaded ? _selectedIds.OrderBy(id => id).ToList() : null;

    /// <summary>阻断不完整定义下的标签修改、跨属性模板旧标签保持以及单选冲突；后端仍最终校验。</summary>
    public string? ValidateSelection(Guid finalProfileId)
    {
        if (IsPopupOpen) return "请先确定或关闭标签选择弹出层。";
        if (!AreDefinitionsLoaded && (IsDirty || (finalProfileId != BaselineProfileId && _baselineIds.Count > 0)))
            return $"{_resolutionMessage} 标签修改已保留，请刷新标签后再保存。";
        if (Groups.Any(group => group.HasSingleSelectionConflict)) return "单选标签组存在多个值，请重新选择或清除。";
        if (MissingSelections.Count > 0 && AreDefinitionsLoaded) return "已选标签在当前属性模板定义中失效，请刷新核对或明确移除后保存。";
        return null;
    }

    /// <summary>用户明确移除失效身份；缓存刷新本身不能执行此操作。</summary>
    [RelayCommand]
    private void RemoveMissingSelection(ProductAttributeMissingSelectionModel? selection)
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
            foreach (var definition in _definitions.DistinctBy(item => item.Uid)
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
        StatusText = !AreDefinitionsLoaded ? $"{_resolutionMessage} 已保存选择保持不变，请刷新标签。"
            : ProfileId == Guid.Empty ? "当前未设置产品属性模板。"
            : MissingSelections.Count > 0 ? "部分已选标签已失效，身份已保留，请核对后明确移除。"
            : Groups.Count == 0 ? "当前属性模板没有标签定义。" : "标签选择随整页保存。";
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(HasSelections));
        OnPropertyChanged(nameof(AreDefinitionsLoaded));
        OnPropertyChanged(nameof(CanEdit));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>接纳当前组已确认值，仅替换本组候选范围，保留其他组及失效选择。</summary>
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

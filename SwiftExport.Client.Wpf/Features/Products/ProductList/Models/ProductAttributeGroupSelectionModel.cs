using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SwiftExport.Contracts.DTOs.Products.Attributes;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System.Collections.ObjectModel;

namespace SwiftExportClient.Features.Products.ProductList.Models;

/// <summary>详情页一个属性组的私有选择草稿；候选只读，弹出层确定后才发布选择。</summary>
public sealed partial class ProductAttributeGroupSelectionModel : ObservableObject
{
    private bool _initializing;
    private readonly ProductAttributeGroupDto _definition;

    /// <summary>从只读定义和已选身份创建本详情独占的选择集合。</summary>
    public ProductAttributeGroupSelectionModel(ProductAttributeGroupDto definition, IEnumerable<Guid> selectedIds)
    {
        _definition = definition;
        Options = (definition.Options ?? []).OrderBy(item => item.SortOrder).ThenBy(item => item.Uid).ToArray();
        var ids = selectedIds.ToHashSet();
        _initializing = true;
        foreach (var option in Options.Where(item => ids.Contains(item.Uid))) SelectedItems.Add(option);
        // 模式从多选变为单选时保留全部已选身份，直到用户明确重新选择或清除。
        SelectedOption = SelectedItems.Count == 1 ? SelectedItems[0] : null;
        _initializing = false;
        UpdateSummary();
    }

    /// <summary>选择明确提交后通知工作区替换本组的有效选项身份。</summary>
    public event EventHandler? SelectionCommitted;
    /// <summary>获取组身份。</summary>
    public Guid Uid => _definition.Uid;
    /// <summary>获取组显示名称。</summary>
    public string GroupName => _definition.GroupName;
    /// <summary>获取组是否为多选。</summary>
    public bool IsMultiple => _definition.SelectionMode == ProductAttributeSelectionMode.Multiple;
    /// <summary>获取组是否为单选。</summary>
    public bool IsSingle => !IsMultiple;
    /// <summary>获取本组只读候选；不在候选 DTO 上保存勾选状态。</summary>
    public IReadOnlyList<ProductAttributeOptionDto> Options { get; }
    /// <summary>获取本详情独占的已确认多选集合，供公共控件写入。</summary>
    public ObservableCollection<ProductAttributeOptionDto> SelectedItems { get; } = [];
    /// <summary>获取或设置单选候选；显式改变时替换本组最终选择。</summary>
    [ObservableProperty] public partial ProductAttributeOptionDto? SelectedOption { get; set; }
    /// <summary>获取已确认选项的可读摘要。</summary>
    [ObservableProperty] public partial string SelectionSummary { get; private set; } = string.Empty;
    /// <summary>获取已有多个值是否与当前单选模式冲突。</summary>
    public bool HasSingleSelectionConflict => IsSingle && SelectedItems.Count > 1;

    partial void OnSelectedOptionChanged(ProductAttributeOptionDto? value)
    {
        if (_initializing || IsMultiple) return;
        SelectedItems.Clear();
        if (value is not null) SelectedItems.Add(value);
        PublishSelection();
    }

    /// <summary>多选控件已写回私有集合后才发布，关闭未确认的弹出层不会调用此命令。</summary>
    [RelayCommand]
    private void CommitMultipleSelection() => PublishSelection();

    /// <summary>单选清除是明确用户操作，也能清除模式变化前遗留的多个值。</summary>
    [RelayCommand]
    private void ClearSingleSelection()
    {
        _initializing = true;
        SelectedOption = null;
        SelectedItems.Clear();
        _initializing = false;
        PublishSelection();
    }

    private void PublishSelection()
    {
        UpdateSummary();
        SelectionCommitted?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateSummary()
    {
        SelectionSummary = SelectedItems.Count == 0 ? "未选择" : string.Join("、", SelectedItems.Select(item => item.OptionName));
        OnPropertyChanged(nameof(HasSingleSelectionConflict));
    }
}

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExportClient.Features.QualityControl.Categories.Models;

namespace SwiftExportClient.Features.QualityControl.Categories.Components;

/// <summary>分类选择器的筛选投影，展开和选择不改变原始分类资料。</summary>
public sealed partial class AfterSalesQualityCategoryLookupNode(AfterSalesQualityCategoryNode source) : ObservableObject
{
    /// <summary>原始分类资料，选择提交返回该身份。</summary>
    public AfterSalesQualityCategoryNode Source { get; } = source;
    /// <summary>包含匹配结果或祖先路径的子节点。</summary>
    public ObservableCollection<AfterSalesQualityCategoryLookupNode> Children { get; } = [];
    [ObservableProperty] public partial bool IsExpanded { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }
}

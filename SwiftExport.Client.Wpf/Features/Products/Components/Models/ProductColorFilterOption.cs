using CommunityToolkit.Mvvm.ComponentModel;
using SwiftExport.Contracts.DTOs.Products;

namespace SwiftExportClient.Features.Products.Components.Models;

/// <summary>产品颜色筛选选项，继承完整颜色资料并保存独立的客户端勾选状态。</summary>
[INotifyPropertyChanged]
public sealed partial class ProductColorFilterOption : ProductColorDto
{
    /// <summary>获取或设置当前颜色是否被筛选控件选中。</summary>
    [ObservableProperty]
    public partial bool IsChecked { get; set; }

    /// <summary>获取优先使用中文、其次英文、最后颜色编码的稳定显示名称。</summary>
    public string DisplayName => !string.IsNullOrWhiteSpace(ColorNameZh)
        ? ColorNameZh
        : !string.IsNullOrWhiteSpace(ColorNameEn)
            ? ColorNameEn
            : ColorCode ?? string.Empty;
}

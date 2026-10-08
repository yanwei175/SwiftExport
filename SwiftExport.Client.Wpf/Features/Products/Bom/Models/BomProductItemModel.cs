using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExportClient.Features.Products.Bom.Models;

/// <summary>管理页只读产品行，保存绑定摘要与数量，不充当产品资料写入模型。</summary>
public sealed partial class BomProductItemModel : ObservableObject
{
    [ObservableProperty] public partial Guid Uid { get; set; }
    [ObservableProperty] public partial int Version { get; set; }
    [ObservableProperty] public partial string MasterSku { get; set; } = string.Empty;
    [ObservableProperty] public partial string Sku { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionEn { get; set; } = string.Empty;
    [ObservableProperty] public partial string DescriptionZh { get; set; } = string.Empty;
    [ObservableProperty] public partial string MainImage { get; set; } = string.Empty;
    [ObservableProperty] public partial string Finish { get; set; } = string.Empty;
    [ObservableProperty] public partial string CategoryName { get; set; } = string.Empty;
    [ObservableProperty] public partial string SupplyType { get; set; } = string.Empty;
    [ObservableProperty] public partial string Unit { get; set; } = string.Empty;
    [ObservableProperty] public partial string BomNo { get; set; } = string.Empty;
    [ObservableProperty] public partial int BomVersion { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText))] public partial bool IsBound { get; set; }
    [ObservableProperty, NotifyPropertyChangedFor(nameof(StatusText))] public partial int ComponentCount { get; set; }
    [ObservableProperty] public partial int SharedProductCount { get; set; }
    /// <summary>绑定条件与组成完整性分别显示，空组成不会冒称完整 BOM。</summary>
    public string StatusText => !IsBound ? "待建" : ComponentCount == 0 ? "待完善" : "已绑定";
}

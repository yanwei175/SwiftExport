using System.Windows.Controls;

namespace SwiftExportClient.Features.Products.Colors.Views;

/// <summary>承载颜色维护页面；行拖拽由共享行为桥接，业务顺序由 ViewModel 维护。</summary>
public partial class ProductColorsView : UserControl
{
    /// <summary>创建颜色维护视图。</summary>
    public ProductColorsView() => InitializeComponent();
}

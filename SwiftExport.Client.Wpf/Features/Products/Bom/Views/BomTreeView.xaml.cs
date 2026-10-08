using System.Windows.Controls;

namespace SwiftExportClient.Features.Products.Bom.Views;

/// <summary>承载 BOM 弹窗内容，输入和生命周期交由对应模型及统一 Dialog 宿主管理。</summary>
public partial class BomTreeView : UserControl
{
    /// <summary>初始化弹窗的只读展示或输入绑定。</summary>
    public BomTreeView() => InitializeComponent();
}

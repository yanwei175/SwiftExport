using System.Windows.Controls;

namespace SwiftExportClient.Features.SystemSettings.SystemParameters.Views;

/// <summary>注册参数的表格视图，逐行保存与草稿保护由 ViewModel 编排。</summary>
public partial class SystemParametersPage : System.Windows.Controls.Page
{
    /// <summary>创建带行内操作的参数表格。</summary>
    public SystemParametersPage() => InitializeComponent();
}

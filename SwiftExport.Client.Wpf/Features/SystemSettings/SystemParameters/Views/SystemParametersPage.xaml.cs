using System.Windows.Controls;

namespace SwiftExportClient.Features.SystemSettings.SystemParameters.Views;

/// <summary>注册系统参数的表单视图，所有读取保存和草稿保护由 ViewModel 编排。</summary>
public partial class SystemParametersPage : System.Windows.Controls.Page
{
    /// <summary>创建参数表单。</summary>
    public SystemParametersPage() => InitializeComponent();
}

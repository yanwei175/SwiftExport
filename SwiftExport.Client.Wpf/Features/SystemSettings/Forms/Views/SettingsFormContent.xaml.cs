using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.Forms.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Forms.Views;

/// <summary>将业务表单正文和同一弹窗内的放弃提示组合，不创建新的 Dialog 宿主。</summary>
public partial class SettingsFormContent : UserControl, IDisposable
{
    /// <summary>绑定本次关闭状态并呈现已由用例构造的表单视图。</summary>
    public SettingsFormContent(FrameworkElement body, SettingsFormState state)
    {
        InitializeComponent();
        DataContext = state;
        FormBody.Content = body;
    }

    /// <summary>当前表单正文，仅供模块级视图组合与交互测试检查。</summary>
    public FrameworkElement? Body => FormBody.Content as FrameworkElement;

    /// <summary>解除正文引用，使密码视图和一次性绑定能及时释放。</summary>
    public void Dispose() => FormBody.Content = null;
}

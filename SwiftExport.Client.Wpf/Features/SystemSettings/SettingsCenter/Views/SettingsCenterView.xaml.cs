using System.Windows.Controls;

namespace SwiftExportClient.Features.SystemSettings.SettingsCenter.Views;

/// <summary>承载设置中心入口按钮，业务动作由 SettingsCenterViewModel 命令处理。</summary>
public partial class SettingsCenterView : UserControl
{
    /// <summary>创建设置中心页签内容。</summary>
    public SettingsCenterView()
    {
        InitializeComponent();
    }
}

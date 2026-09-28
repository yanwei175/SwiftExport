using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.Dictionaries.ViewModels;
using SwiftExportClient.Features.SystemSettings.Dictionaries.Views;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>将现有字典编辑视图嵌入 NavigationView 的 Page，不复制字典业务和编辑状态。</summary>
public partial class DictionaryManagementPage : Page
{
    /// <summary>共享既有字典 ViewModel，页面只负责 WPF 导航宿主。</summary>
    public DictionaryManagementPage(DictionaryManagerViewModel model)
    {
        InitializeComponent();
        DictionaryView.DataContext = model;
    }

    /// <summary>提供现有 DataGrid 编辑提交入口，切页和关闭时仍由宿主统一门禁。</summary>
    public DictionaryManagerView Editor => DictionaryView;
}

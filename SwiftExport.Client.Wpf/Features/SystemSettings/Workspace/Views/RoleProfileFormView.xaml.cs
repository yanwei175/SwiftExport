using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>按创建或编辑模式只提交角色资料，权限分配由独立表单负责。</summary>
public partial class RoleProfileFormView : UserControl
{
    private readonly RbacManagementViewModel _model;
    private readonly bool _creating;

    /// <summary>新建时使用独立草稿字段，编辑时使用选中角色的版本字段。</summary>
    public RoleProfileFormView(RbacManagementViewModel model, bool creating)
    {
        InitializeComponent();
        _model = model;
        _creating = creating;
        DataContext = model;
        CreateFields.Visibility = creating ? Visibility.Visible : Visibility.Collapsed;
        EditFields.Visibility = creating ? Visibility.Collapsed : Visibility.Visible;
        SaveButton.Content = creating ? "创建角色" : "保存资料";
    }

    /// <summary>后端写入成功且页面刷新完成后触发。</summary>
    public event EventHandler? Saved;

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_model.IsWriting || _model.IsBusy) return;
        var command = _creating ? _model.CreateRoleCommand : _model.SaveRoleProfileCommand;
        if (!command.CanExecute(null)) return;
        var revision = _model.SuccessfulWriteRevision;
        SaveButton.IsEnabled = false;
        try { await command.ExecuteAsync(null); }
        finally { SaveButton.IsEnabled = true; }
        if (_model.SuccessfulWriteRevision != revision) Saved?.Invoke(this, EventArgs.Empty);
    }
}

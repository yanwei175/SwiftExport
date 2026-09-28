using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>编辑用户角色完整集合；筛选或显示不改变提交集合。</summary>
public partial class UserRolesFormView : UserControl
{
    private readonly RbacManagementViewModel _model;

    /// <summary>复用已读取的用户角色版本和有效权限。</summary>
    public UserRolesFormView(RbacManagementViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
    }

    /// <summary>整组角色保存成功后触发。</summary>
    public event EventHandler? Saved;

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_model.IsWriting || _model.IsBusy || !_model.SaveUserRolesCommand.CanExecute(null)) return;
        var revision = _model.SuccessfulWriteRevision;
        SaveButton.IsEnabled = false;
        try { await _model.SaveUserRolesCommand.ExecuteAsync(null); }
        finally { SaveButton.IsEnabled = true; }
        if (_model.SuccessfulWriteRevision != revision) Saved?.Invoke(this, EventArgs.Empty);
    }
}

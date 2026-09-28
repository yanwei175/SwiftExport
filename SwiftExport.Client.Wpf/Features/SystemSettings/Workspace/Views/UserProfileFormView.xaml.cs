using System.Windows;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>仅编辑选中用户的非凭据资料，并在对应写入成功后通知宿主关闭。</summary>
public partial class UserProfileFormView : UserControl
{
    private readonly RbacManagementViewModel _model;

    /// <summary>绑定已加载的用户聚合，不在表单内另发选择请求。</summary>
    public UserProfileFormView(RbacManagementViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
    }

    /// <summary>当前保存范围成功写入时触发。</summary>
    public event EventHandler? Saved;

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_model.IsWriting || _model.IsBusy || !_model.SaveUserProfileCommand.CanExecute(null)) return;
        var revision = _model.SuccessfulWriteRevision;
        SaveButton.IsEnabled = false;
        try { await _model.SaveUserProfileCommand.ExecuteAsync(null); }
        finally { SaveButton.IsEnabled = true; }
        if (_model.SuccessfulWriteRevision != revision) Saved?.Invoke(this, EventArgs.Empty);
    }
}

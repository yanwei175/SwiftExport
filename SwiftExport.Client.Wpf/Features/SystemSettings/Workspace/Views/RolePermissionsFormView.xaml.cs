using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Models;
using SwiftExportClient.Features.SystemSettings.RbacManagement.ViewModels;

namespace SwiftExportClient.Features.SystemSettings.Workspace.Views;

/// <summary>筛选仅影响展示，保存仍由 RBAC ViewModel 发送完整可分配权限集合。</summary>
public partial class RolePermissionsFormView : UserControl
{
    private readonly RbacManagementViewModel _model;
    private readonly ListCollectionView _choices;

    /// <summary>绑定已读取的角色权限版本，不创建第二套授权状态。</summary>
    public RolePermissionsFormView(RbacManagementViewModel model)
    {
        InitializeComponent();
        _model = model;
        DataContext = model;
        // 每个弹窗独享筛选视图，不保留上一次弹窗的搜索条件。
        _choices = new ListCollectionView(model.RolePermissionChoices);
        PermissionItems.ItemsSource = _choices;
    }

    /// <summary>完整权限集合保存成功后触发。</summary>
    public event EventHandler? Saved;

    private void OnFilterChanged(object sender, TextChangedEventArgs e)
    {
        if (_choices is null) return;
        _choices.Filter =
            item => item is RbacChoiceModel choice
                && (string.IsNullOrWhiteSpace(PermissionSearch.Text)
                    || choice.DisplayName.Contains(PermissionSearch.Text, StringComparison.OrdinalIgnoreCase));
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_model.IsWriting || _model.IsBusy || !_model.SaveRolePermissionsCommand.CanExecute(null)) return;
        var revision = _model.SuccessfulWriteRevision;
        SaveButton.IsEnabled = false;
        try { await _model.SaveRolePermissionsCommand.ExecuteAsync(null); }
        finally { SaveButton.IsEnabled = true; }
        if (_model.SuccessfulWriteRevision != revision) Saved?.Invoke(this, EventArgs.Empty);
    }
}

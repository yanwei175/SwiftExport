using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.Views;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.ViewModels;
namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Roles.PermissionAssignment.Views;

/// <summary>呈现当前用例的输入内容；保存、筛选与关闭由 ViewModel 负责。</summary>
public partial class RolePermissionsFormView : UserControl
{
    /// <summary>绑定已准备好的用例模型，不在视图内提交业务命令。</summary>
    public RolePermissionsFormView(RolePermissionsFormViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.Views;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.ViewModels;
namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Users.RoleAssignment.Views;

/// <summary>呈现当前用例的输入内容；保存、筛选与关闭由 ViewModel 负责。</summary>
public partial class UserRolesFormView : UserControl
{
    /// <summary>绑定已准备好的用例模型，不在视图内提交业务命令。</summary>
    public UserRolesFormView(UserRolesFormViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

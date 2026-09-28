using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.Views;
using System.Windows.Controls;
using SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.ViewModels;
namespace SwiftExportClient.Features.SystemSettings.RbacManagement.Users.Profile.Views;

/// <summary>呈现当前用例的输入内容；保存、筛选与关闭由 ViewModel 负责。</summary>
public partial class UserProfileFormView : UserControl
{
    /// <summary>绑定已准备好的用例模型，不在视图内提交业务命令。</summary>
    public UserProfileFormView(UserProfileFormViewModel model)
    {
        InitializeComponent();
        DataContext = model;
    }
}

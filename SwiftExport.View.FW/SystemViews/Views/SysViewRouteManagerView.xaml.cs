using SwiftExport.View.FW.SystemViews.ViewModels;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SystemViews.Views
{
    /// <summary>
    /// 视图路由配置管理页 Code-Behind（无额外逻辑）
    /// </summary>
    public partial class SysViewRouteManagerView : UserControl
    {
        public SysViewRouteManagerView()
        {
            InitializeComponent();
        }
        public SysViewRouteManagerView(SysViewRouteManagerViewModel vm):this()
        {
            this.DataContext=vm;
        }
    }
}

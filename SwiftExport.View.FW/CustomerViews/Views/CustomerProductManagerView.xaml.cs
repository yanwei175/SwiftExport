using System.Windows.Controls;

namespace SwiftExport.View.FW.CustomerViews.Views
{
    /// <summary>
    /// 客户产品管理子视图，嵌入 CustomerManagerView 的"客户产品管理"TabItem 中使用。
    /// DataContext 由父视图通过 code-behind 绑定 CustomerProductManagerViewModel。
    /// </summary>
    public partial class CustomerProductManagerView : UserControl
    {
        public CustomerProductManagerView()
        {
            InitializeComponent();
        }
    }
}

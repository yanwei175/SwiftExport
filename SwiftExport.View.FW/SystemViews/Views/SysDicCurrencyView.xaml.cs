using SwiftExport.View.FW.SystemViews.ViewModels;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SystemViews.Views
{
    /// <summary>
    /// 货币管理视图后台代码
    /// ViewModel 通过构造函数注入（DI 驱动）
    /// </summary>
    public partial class SysDicCurrencyView : UserControl
    {
        public SysDicCurrencyView()
        {
            InitializeComponent();
        }
        public SysDicCurrencyView(SysDicCurrencyViewModel vm):this() 
        {
            this.DataContext = vm;
        }
    }
}

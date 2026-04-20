using SwiftExport.View.FW.DictViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// 字典类型管理独立窗体后台代码
    /// ViewModel 通过构造函数注入（DI 驱动）
    /// </summary>
    public partial class DictTypeManagerView : Window
    {
        public DictTypeManagerView(DictTypeManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}

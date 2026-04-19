using SwiftExport.View.FW.DictViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// 中文注释：字典类型管理窗体
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

using SwiftExport.View.FW.DictViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// 中文注释：字典值配置窗体
    /// </summary>
    public partial class DictDataManagerView : Window
    {
        public DictDataManagerView(DictDataManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }
    }
}

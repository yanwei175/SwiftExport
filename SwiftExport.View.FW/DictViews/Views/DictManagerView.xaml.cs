using SwiftExport.View.FW.DictViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// DictManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class DictManagerView : Window
    {
        private readonly DictTypeManagerView _dictTypeManagerView;
        private readonly DictDataManagerView _dictDataManagerView;

        public DictManagerView(DictTypeManagerView dictTypeManagerView, DictDataManagerView dictDataManagerView)
        {
            InitializeComponent();
            _dictTypeManagerView = dictTypeManagerView;
            _dictDataManagerView = dictDataManagerView;
        }

        // 中文注释：打开字典类型管理窗体
        private void OpenTypeManager_Click(object sender, RoutedEventArgs e)
        {
            _dictTypeManagerView.Owner = this;
            _dictTypeManagerView.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _dictTypeManagerView.ShowDialog();
        }

        // 中文注释：打开字典值配置窗体
        private void OpenDataManager_Click(object sender, RoutedEventArgs e)
        {
            _dictDataManagerView.Owner = this;
            _dictDataManagerView.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            _dictDataManagerView.ShowDialog();
        }
    }
}

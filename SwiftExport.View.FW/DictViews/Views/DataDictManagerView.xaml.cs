using SwiftExport.View.FW.DictViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// 中文注释：主数据与系统字典统一管理窗体
    /// 左侧使用新的树形模型（DictTypeTreeListModel + DictTypeTreeModel）
    /// 右侧动态加载字典明细 ViewModel
    /// </summary>
    public partial class DataDictManagerView : UserControl
    {
        public DataDictManagerView(DataDictManagerViewModel vm)
        {
            InitializeComponent();
            DataContext = vm;
        }

        /// <summary>
        /// 全部展开事件处理
        /// </summary>
        private void ExpandAll_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as DataDictManagerViewModel)?.ExpandAllNodes();
        }

        /// <summary>
        /// 全部折叠事件处理
        /// </summary>
        private void CollapseAll_Click(object sender, RoutedEventArgs e)
        {
            (DataContext as DataDictManagerViewModel)?.CollapseAllNodes();
        }
    }
}

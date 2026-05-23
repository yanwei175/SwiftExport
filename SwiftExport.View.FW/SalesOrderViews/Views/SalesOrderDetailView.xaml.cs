using SwiftExport.View.FW.SalesOrderViews.Models;
using SwiftExport.View.FW.SalesOrderViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SalesOrderViews.Views
{
    /// <summary>
    /// SalesOrderDetailView.xaml 的交互逻辑
    /// 销售订单详情编辑弹窗（支持新增/编辑/查看）
    /// </summary>
    public partial class SalesOrderDetailView : UserControl
    {
        public SalesOrderDetailView()
        {
            InitializeComponent();
        }

        public SalesOrderDetailView(SalesOrderDetailViewModel vm) : this()
        {

            this.DataContext = vm;
        }

        // 中文注释：弹窗关闭时返回 true 表示用户按了"保存"并成功提交
        // 返回 false 或不返回表示用户取消了编辑
        public bool? DialogResult_Custom { get; set; }
    }
}

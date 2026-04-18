using SwiftExport.VSTO.UserWindow.CustomerViews.ViewModels;
using System.Windows;

namespace SwiftExport.VSTO.UserWindow.CustomerViews.Views
{
    /// <summary>
    /// CustomerPoConfigManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class CustomerPoConfigManagerView : Window
    {
        //// 记录当前正在编辑的行实体
        //private CustomerPoConfigModel _currentRowModel;
        //// 记录当前操作的列类型 ("Customer" 或 "Supplier")
        //private string _currentLookupType;
        //// ★ 防双击死锁的时间戳
        //private DateTime _lastPopupClickTime = DateTime.MinValue;

        public CustomerPoConfigManagerView(CustomerPoConfigViewModel vm)
        {
            InitializeComponent();
            this.DataContext = vm;
        }

        // 关闭窗体
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

    }
}
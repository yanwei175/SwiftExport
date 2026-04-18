using SwiftExport.View.FW.CustomerViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.CustomerViews.Views
{
    /// <summary>
    /// CustomerManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class CustomerManagerView : UserControl
    {
        public CustomerManagerView(CustomerManagerViewModel vm)
        {
            InitializeComponent();
            this.DataContext = vm;
        }


        // 关闭窗体
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null)
            {
                window.Close();
            }
        }
    }
}

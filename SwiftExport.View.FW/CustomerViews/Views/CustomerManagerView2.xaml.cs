using SwiftExport.View.FW.CustomerViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.CustomerViews.Views
{
    /// <summary>
    /// CustomerManagerView2.xaml 的交互逻辑
    /// </summary>
    public partial class CustomerManagerView2 : UserControl
    {
        public CustomerManagerView2(CustomerManagerVM2 vm)
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

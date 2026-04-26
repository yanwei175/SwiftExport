using SwiftExport.View.FW.ProductViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.ProductViews.Views
{
    /// <summary>
    /// ProductManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class MainProductManagerView : UserControl
    {
        public MainProductManagerView()
        {
            InitializeComponent();
        }
        public MainProductManagerView(MainProductManagerViewModel vm) : this()
        {
            this.DataContext = vm;
        }

    }
}

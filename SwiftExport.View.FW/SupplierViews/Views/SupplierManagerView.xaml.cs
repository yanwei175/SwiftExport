using SwiftExport.View.FW.SupplierViews.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExport.View.FW.SupplierViews.Views
{
    /// <summary>
    /// SupplierManagerView.xaml 的交互逻辑。
    /// </summary>
    public partial class SupplierManagerView : UserControl
    {
        public SupplierManagerView()
        {
            InitializeComponent();
        }

        public SupplierManagerView(SupplierManagerViewModel vm) : this()
        {
            this.DataContext = vm;
        }
    }
}

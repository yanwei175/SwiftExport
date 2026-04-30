using SwiftExport.View.FW.PurchaseOrderViews.ViewModels;
using System.Windows.Controls;

namespace SwiftExport.View.FW.PurchaseOrderViews.Views
{
    /// <summary>
    /// PurchaseOrderManagerView.xaml 的交互逻辑。
    /// </summary>
    public partial class PurchaseOrderManagerView : UserControl
    {
        public PurchaseOrderManagerView()
        {
            InitializeComponent();
        }

        public PurchaseOrderManagerView(PurchaseOrderManagerViewModel vm) : this()
        {
            DataContext = vm;
        }
    }
}

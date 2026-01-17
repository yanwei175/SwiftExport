using SwiftExport.UI.ViewModels;
using System.Windows.Controls;

namespace SwiftExport.UI.Views
{
    /// <summary>
    /// ProductColorView.xaml 的交互逻辑
    /// </summary>
    public partial class ProductColorView : UserControl
    {
        public ProductColorView()
        {
            InitializeComponent();
            DataContext = new ProductColorViewModel();
        }
    }
}

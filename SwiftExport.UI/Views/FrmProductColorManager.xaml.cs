using SwiftExport.UI.ViewModels;
using System.Windows.Controls;

namespace SwiftExport.UI.Views
{
    /// <summary>
    /// FrmProductColorManager.xaml 的交互逻辑
    /// </summary>
    public partial class FrmProductColorManager : UserControl
    {
        public FrmProductColorManager()
        {
            InitializeComponent();
            DataContext = new ProductColorViewModel();
        }
    }
}

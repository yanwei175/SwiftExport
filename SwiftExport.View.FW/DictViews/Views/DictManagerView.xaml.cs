using SwiftExport.View.FW.DictViews.ViewModels;
using System.Windows;

namespace SwiftExport.View.FW.DictViews.Views
{
    /// <summary>
    /// DictManagerView.xaml 的交互逻辑
    /// </summary>
    public partial class DictManagerView : Window
    {
        public DictManagerView(DictManagerViewModel vm)
        {
            InitializeComponent();
            this.DataContext = vm;
        }
    }
}

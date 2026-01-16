using SwiftExport.UI.Views;
using System.Windows;

namespace SwiftExport.Desktop
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            var ustr = new FrmProductColorManager();
            this.Content = ustr;
        }
    }
}
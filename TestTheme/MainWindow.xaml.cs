using System.Windows;

namespace TestTheme
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
        private void Border_Loaded(object sender, RoutedEventArgs e)
        {
            var res = TryFindResource("ScaleInStoryboard");
            MessageBox.Show(res == null ? "NOT FOUND" : "FOUND");
        }
    }
}

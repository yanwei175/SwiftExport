using SwiftExport.View.FW.SystemViews.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace SwiftExport.View.FW.SystemViews.Views
{
    /// <summary>
    /// ResetPasswdView.xaml 的交互逻辑
    /// </summary>
    public partial class ResetPasswdView : Window
    {
        public ResetPasswdView()
        {
            InitializeComponent();
        }

        public ResetPasswdView(ResetPasswdViewModel vm) : this()
        {
            DataContext = vm;

            // 绑定确认按钮点击到 ViewModel 的确认命令
            ConfirmBtn.Click += (s, e) =>
            {
                if (vm.ConfirmCommand.CanExecute(null))
                    vm.ConfirmCommand.Execute(null);
            };

            // 取消按钮：直接关闭窗口
            CancelBtn.Click += (s, e) => Close();

            // 重置成功后自动关闭窗口
            vm.ResetSucceeded += () =>
            {
                DialogResult = true;
                Close();
            };
        }

        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                this.DragMove(); // 允许用户点击背景拖动窗口
            }
        }
    }
}

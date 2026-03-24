using System.Windows;
using System.Windows.Input;

namespace SwiftExport.View.FW.Dialog
{
    public partial class InputBoxWindow : Window
    {
        // 存储用户输入的结果
        public string InputText { get; private set; } = null;

        // 判断是否点击了确定
        public bool IsConfirmed { get; private set; } = false;

        public InputBoxWindow(string message, string title, string defaultText = "")
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;
            TxtInput.Text = defaultText;
        }

        // 窗口加载时，自动聚焦输入框并全选文本
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            TxtInput.Focus();
            if (!string.IsNullOrEmpty(TxtInput.Text))
            {
                TxtInput.SelectAll();
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            InputText = TxtInput.Text;
            IsConfirmed = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            IsConfirmed = false;
            InputText = null;
            this.Close();
        }

        // 允许拖动窗口
        private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        // 处理回车键 (虽然 Button IsDefault 已经处理了，但为了保险起见双重处理)
        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnOk_Click(sender, e);
            }
        }
    }
}
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace SwiftExport.View.FW.Dialog
{
    /// <summary>
    /// 弹窗类型枚举
    /// </summary>
    public enum MsgType
    {
        Info,       // 绿色图标，只有确定
        Warning,    // 黄色图标，只有确定
        Error,      // 红色图标，只有确定
        Question    // 蓝色图标，确定/取消
    }

    public partial class MsgBoxWindow : Window
    {
        public bool Result { get; private set; } = false;

        public MsgBoxWindow(string message, string title, MsgType type)
        {
            InitializeComponent();
            TxtMessage.Text = message;
            TxtTitle.Text = title;

            // 根据类型切换 UI 状态
            switch (type)
            {
                case MsgType.Info:
                    IconInfo.Visibility = Visibility.Visible;
                    BtnOkOnly.Visibility = Visibility.Visible;
                    // 设置主色调为 Teal
                    BtnOkOnly.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#009688"));
                    BtnOkOnly.BorderBrush = BtnOkOnly.Background;
                    break;

                case MsgType.Warning:
                    IconWarn.Visibility = Visibility.Visible;
                    BtnOkOnly.Visibility = Visibility.Visible;
                    // 设置主色调为 Orange
                    BtnOkOnly.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF9800"));
                    BtnOkOnly.BorderBrush = BtnOkOnly.Background;
                    break;

                case MsgType.Error:
                    IconError.Visibility = Visibility.Visible;
                    BtnOkOnly.Visibility = Visibility.Visible;
                    // 设置主色调为 Red
                    BtnOkOnly.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F44336"));
                    BtnOkOnly.BorderBrush = BtnOkOnly.Background;
                    break;

                case MsgType.Question:
                    IconQuestion.Visibility = Visibility.Visible;
                    PanelYesNo.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            Result = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Result = false;
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
    }
}
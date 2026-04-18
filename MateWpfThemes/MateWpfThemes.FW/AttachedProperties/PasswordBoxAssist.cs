using System.Windows;
using System.Windows.Controls;

namespace MateWpfThemes.FW.AttachedProperties
{
    public static class PasswordBoxAssist
    {
        // ==========================================
        // 1. 状态发射器：告诉 XAML 密码框是不是空的
        // ==========================================
        public static readonly DependencyProperty IsPasswordEmptyProperty =
            DependencyProperty.RegisterAttached(
                "IsPasswordEmpty", typeof(bool), typeof(PasswordBoxAssist), new PropertyMetadata(true));

        public static void SetIsPasswordEmpty(DependencyObject element, bool value) => element.SetValue(IsPasswordEmptyProperty, value);
        public static bool GetIsPasswordEmpty(DependencyObject element) => (bool)element.GetValue(IsPasswordEmptyProperty);

        // ==========================================
        // 2. 监听开关：一旦开启，就开始监听密码变化
        // ==========================================
        public static readonly DependencyProperty AttachProperty =
            DependencyProperty.RegisterAttached(
                "Attach", typeof(bool), typeof(PasswordBoxAssist), new PropertyMetadata(false, OnAttachChanged));

        public static void SetAttach(DependencyObject element, bool value) => element.SetValue(AttachProperty, value);
        public static bool GetAttach(DependencyObject element) => (bool)element.GetValue(AttachProperty);

        private static void OnAttachChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PasswordBox passwordBox)
            {
                if ((bool)e.NewValue)
                {
                    // 挂载监听
                    passwordBox.PasswordChanged += PasswordBox_PasswordChanged;
                    // 初始化一下当前状态
                    SetIsPasswordEmpty(passwordBox, string.IsNullOrEmpty(passwordBox.Password));
                }
                else
                {
                    // 卸载监听
                    passwordBox.PasswordChanged -= PasswordBox_PasswordChanged;
                }
            }
        }

        private static void PasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (sender is PasswordBox passwordBox)
            {
                // 密码一变，马上更新空状态
                SetIsPasswordEmpty(passwordBox, string.IsNullOrEmpty(passwordBox.Password));
            }
        }
    }
}
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SwiftExport.UI.Controls.Expand
{
    public static class ExcelEnterBehavior
    {
        // 注册一个附加属性，相当于给 TextBox 贴个标签
        public static readonly DependencyProperty IsExcelMultilineEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsExcelMultilineEnabled",
                typeof(bool),
                typeof(ExcelEnterBehavior),
                new PropertyMetadata(false, OnIsExcelMultilineEnabledChanged));

        public static void SetIsExcelMultilineEnabled(DependencyObject element, bool value)
        {
            element.SetValue(IsExcelMultilineEnabledProperty, value);
        }

        public static bool GetIsExcelMultilineEnabled(DependencyObject element)
        {
            return (bool)element.GetValue(IsExcelMultilineEnabledProperty);
        }

        private static void OnIsExcelMultilineEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                if ((bool)e.NewValue)
                {
                    // 开启：挂载按键拦截事件
                    textBox.PreviewKeyDown += TextBox_PreviewKeyDown;
                }
                else
                {
                    // 关闭：卸载事件
                    textBox.PreviewKeyDown -= TextBox_PreviewKeyDown;
                }
            }
        }

        private static void TextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                bool isModifierDown = Keyboard.IsKeyDown(Key.LeftAlt) ||
                              Keyboard.IsKeyDown(Key.RightAlt) ||
                              Keyboard.IsKeyDown(Key.LeftCtrl) ||
                              Keyboard.IsKeyDown(Key.LeftShift);
                // ★ 核心逻辑：拦截 Alt + Enter (或者 Shift+Enter)
                // 你可以根据用户的习惯把 ModifierKeys.Alt 改成 ModifierKeys.Shift
                if (e.Key == Key.Enter && isModifierDown)
                {
                    // 1. 拿到当前光标位置
                    int selectionStart = textBox.SelectionStart;

                    // 2. 在光标位置强行插入一个系统换行符 (\r\n)
                    textBox.SelectedText = Environment.NewLine;

                    // 3. 把光标挪到换行符的后面
                    textBox.SelectionStart = selectionStart + Environment.NewLine.Length;
                    textBox.SelectionLength = 0;

                    // 4. 告诉 WPF：“这个回车我吃掉了，你 DataGrid 不要去跳下一行！”
                    e.Handled = true;
                }
            }
        }
    }
}
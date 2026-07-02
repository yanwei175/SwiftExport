
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    public static class TextBoxAssist
    {

        // ==========================================
        // Placeholder: 占位符文本 (例如: "请输入客户代码...")
        // ==========================================
        #region Placeholder: 占位符文本 (例如: "请输入客户代码...")
        public static readonly DependencyProperty PlaceholderProperty =
            DependencyProperty.RegisterAttached(
                "Placeholder", typeof(string), typeof(TextBoxAssist), new PropertyMetadata(string.Empty));

        public static void SetPlaceholder(DependencyObject element, string value) => element.SetValue(PlaceholderProperty, value);
        public static string GetPlaceholder(DependencyObject element) => (string)element.GetValue(PlaceholderProperty);
        #endregion

        // ==========================================
        // ShowClearButton: 是否显示右侧的一键清空(X)按钮
        // ==========================================
        #region ShowClearButton: 是否显示右侧的一键清空(X)按钮
        public static readonly DependencyProperty ShowClearButtonProperty =
                                            DependencyProperty.RegisterAttached("ShowClearButton", typeof(bool), typeof(TextBoxAssist),
                                            new PropertyMetadata(false, OnShowClearButtonChanged)); // 加上回调函数
        private static void OnShowClearButtonChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                textBox.Loaded -= TextBox_Loaded; // 防止重复挂载
                if ((bool)e.NewValue)
                {
                    textBox.Loaded += TextBox_Loaded;
                }
            }
        }
        private static void TextBox_Loaded(object sender, RoutedEventArgs e)
        {
            var textBox = sender as TextBox;
            // 从 Template 中找到我们刚才写的 PART_ClearButton
            if (textBox?.Template.FindName("PART_ClearButton", textBox) is System.Windows.Controls.Button clearButton)
            {
                // 绑定点击事件，一键清空文字！
                clearButton.Click -= ClearButton_Click;
                clearButton.Click += ClearButton_Click;
            }
        }

        private static void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button button && button.TemplatedParent is TextBox textBox)
            {
                textBox.Text = string.Empty;
                textBox.Focus(); // 清空后自动获取光标，体验极佳
            }
        }

        public static void SetShowClearButton(DependencyObject element, bool value) => element.SetValue(ShowClearButtonProperty, value);
        public static bool GetShowClearButton(DependencyObject element) => (bool)element.GetValue(ShowClearButtonProperty);
        #endregion

        // ==========================================
        // FocusBorderBrush: 获取焦点时的边框/下划线高亮颜色
        // ==========================================
        #region FocusBorderBrush: 获取焦点时的边框/下划线高亮颜色
        /// <summary>
        /// 获取焦点时的边框/下划线高亮颜色
        /// </summary>
        public static readonly DependencyProperty FocusBorderBrushProperty =
            DependencyProperty.RegisterAttached(
                "FocusBorderBrush", typeof(Brush), typeof(TextBoxAssist), new PropertyMetadata(null));

        public static void SetFocusBorderBrush(DependencyObject element, Brush value) => element.SetValue(FocusBorderBrushProperty, value);
        public static Brush GetFocusBorderBrush(DependencyObject element) => (Brush)element.GetValue(FocusBorderBrushProperty);
        #endregion


        // ==========================================
        // ExcelEnterBehavior: 允许使用 Excel 的多行输入方式
        // ==========================================
        #region ExcelEnterBehavior: 允许使用 Excel 的多行输入方式

        public static readonly DependencyProperty AllowAltEnterNewlineProperty =
            DependencyProperty.RegisterAttached(
                "AllowAltEnterNewline",
                typeof(bool),
                typeof(TextBoxAssist),
                new PropertyMetadata(false, OnAllowAltEnterNewlineChanged));

        public static void SetAllowAltEnterNewline(DependencyObject element, bool value)
        {
            element.SetValue(AllowAltEnterNewlineProperty, value);
        }

        public static bool GetAllowAltEnterNewline(DependencyObject element)
        {
            return (bool)element.GetValue(AllowAltEnterNewlineProperty);
        }

        private static void OnAllowAltEnterNewlineChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
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
        #endregion

    }
}

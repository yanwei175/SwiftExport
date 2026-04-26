using Microsoft.Xaml.Behaviors;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// ============================================================
    /// DataGridTextColumnAssist —— DataGridTextColumn 的 Popup 行为桥接器
    /// ============================================================
    ///
    /// 设计目的：
    /// 1) 让 DataGridTextColumn 通过 EditingElementStyle 使用 Popup 搜索行为；
    /// 2) 保留 DataGridTextColumn 的原生特性（排序、复制、提交流程等）；
    /// 3) 避免在 XAML 中必须写 <i:Interaction.Behaviors> 的模板列写法。
    ///
    /// 使用方式：
    /// 在 TextBox（尤其是 DataGridTextColumn 的 EditingElementStyle）上设置：
    /// - IsEnabled = true
    /// - Popup
    /// - SearchCommand
    /// - CommitCommand
    ///
    /// 桥接器会自动创建/复用 TextBoxPopupAssist，并同步参数。
    /// ============================================================
    /// </summary>
    public static class DataGridTextColumnAssist
    {
        #region IsEnabled

        /// <summary>
        /// 是否启用 Popup 搜索行为。
        /// </summary>
        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(DataGridTextColumnAssist),
                new PropertyMetadata(false, OnAssistPropertyChanged));

        public static void SetIsEnabled(DependencyObject obj, bool value)
            => obj.SetValue(IsEnabledProperty, value);

        public static bool GetIsEnabled(DependencyObject obj)
            => (bool)obj.GetValue(IsEnabledProperty);

        #endregion

        #region Popup

        /// <summary>
        /// 共享 Popup 实例（必须绑定）。
        /// </summary>
        public static readonly DependencyProperty PopupProperty =
            DependencyProperty.RegisterAttached(
                "Popup",
                typeof(Popup),
                typeof(DataGridTextColumnAssist),
                new PropertyMetadata(null, OnAssistPropertyChanged));

        public static void SetPopup(DependencyObject obj, Popup value)
            => obj.SetValue(PopupProperty, value);

        public static Popup GetPopup(DependencyObject obj)
            => (Popup)obj.GetValue(PopupProperty);

        #endregion

        #region SearchCommand

        /// <summary>
        /// 搜索命令（参数 string keyword）。
        /// </summary>
        public static readonly DependencyProperty SearchCommandProperty =
            DependencyProperty.RegisterAttached(
                "SearchCommand",
                typeof(ICommand),
                typeof(DataGridTextColumnAssist),
                new PropertyMetadata(null, OnAssistPropertyChanged));

        public static void SetSearchCommand(DependencyObject obj, ICommand value)
            => obj.SetValue(SearchCommandProperty, value);

        public static ICommand GetSearchCommand(DependencyObject obj)
            => (ICommand)obj.GetValue(SearchCommandProperty);

        #endregion

        #region CommitCommand

        /// <summary>
        /// 提交命令（参数为当前 DataGrid 行对象）。
        /// </summary>
        public static readonly DependencyProperty CommitCommandProperty =
            DependencyProperty.RegisterAttached(
                "CommitCommand",
                typeof(ICommand),
                typeof(DataGridTextColumnAssist),
                new PropertyMetadata(null, OnAssistPropertyChanged));

        public static void SetCommitCommand(DependencyObject obj, ICommand value)
            => obj.SetValue(CommitCommandProperty, value);

        public static ICommand GetCommitCommand(DependencyObject obj)
            => (ICommand)obj.GetValue(CommitCommandProperty);

        #endregion

        /// <summary>
        /// 任意桥接属性变化后，统一重新同步行为实例。
        /// </summary>
        private static void OnAssistPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var textBox = d as TextBox;
            if (textBox == null) return;

            SyncBehavior(textBox);
        }

        /// <summary>
        /// 同步 TextBox 上的 TextBoxPopupAssist：
        /// - 未启用时移除行为
        /// - 启用时创建或复用行为并更新参数
        /// </summary>
        private static void SyncBehavior(TextBox textBox)
        {
            var behaviors = Interaction.GetBehaviors(textBox);
            var behavior = behaviors.OfType<TextBoxPopupAssist>().FirstOrDefault();

            if (!GetIsEnabled(textBox))
            {
                if (behavior != null)
                {
                    behaviors.Remove(behavior);
                }
                return;
            }

            if (behavior == null)
            {
                behavior = new TextBoxPopupAssist();
                behaviors.Add(behavior);
            }

            // 中文注释：每次同步都覆盖最新绑定值，确保编辑控件复用时参数正确。
            behavior.Popup = GetPopup(textBox);
            behavior.SearchCommand = GetSearchCommand(textBox);
            behavior.CommitCommand = GetCommitCommand(textBox);
        }
    }
}

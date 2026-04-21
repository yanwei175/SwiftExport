using MateWpfThemes.FW.Services.Dialogs;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace MateWpfThemes.FW.Controls
{
    public class DialogHost : Control
    {
        // Toast 容器控件的模板子控件名称
        private const string PART_ToastContainerName = "PART_ToastContainer";

        static DialogHost()
        {
            // 告诉 WPF 引擎：去 Themes/Generic.xaml 找我的长相
            DefaultStyleKeyProperty.OverrideMetadata(typeof(DialogHost), new FrameworkPropertyMetadata(typeof(DialogHost)));
        }

        public DialogHost()
        {
            // 实例化时，主动向全局服务报到
            DialogProvider.RegisterHost(this);
        }

        // ─────────────────────────────────────────────
        // 模态弹窗相关属性
        // ─────────────────────────────────────────────

        // 控制遮罩是否显示的开关
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register(
            "IsOpen", typeof(bool), typeof(DialogHost), new PropertyMetadata(false));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        // 装载具体弹窗内容的槽位
        public static readonly DependencyProperty DialogContentProperty = DependencyProperty.Register(
            "DialogContent", typeof(object), typeof(DialogHost), new PropertyMetadata(null));

        public object DialogContent
        {
            get => GetValue(DialogContentProperty);
            set => SetValue(DialogContentProperty, value);
        }

        // ─────────────────────────────────────────────
        // Toast 通知队列
        // ─────────────────────────────────────────────

        /// <summary>
        /// 当前激活的 Toast 通知集合，供模板中的 PART_ToastContainer 绑定使用
        /// </summary>
        internal ObservableCollection<MsgBoxViewModel> ActiveToasts { get; }
            = new ObservableCollection<MsgBoxViewModel>();

        // ─────────────────────────────────────────────
        // 模板应用：将 PART_ToastContainer 与集合绑定
        // ─────────────────────────────────────────────

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();

            // 找到模板中声明的 Toast 容器，将其 ItemsSource 绑定到内部集合
            if (GetTemplateChild(PART_ToastContainerName) is ItemsControl toastContainer)
            {
                toastContainer.ItemsSource = ActiveToasts;
            }
        }
    }
}
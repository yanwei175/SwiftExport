using MateWpfThemes.FW.Services.Dialogs;
using System.Windows;
using System.Windows.Controls;

namespace MateWpfThemes.FW.Controls
{
    public class DialogHost : Control
    {
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

        // 控制遮罩是否显示的开关
        public static readonly DependencyProperty IsOpenProperty = DependencyProperty.Register("IsOpen", typeof(bool), typeof(DialogHost), new PropertyMetadata(false));

        public bool IsOpen
        {
            get => (bool)GetValue(IsOpenProperty);
            set => SetValue(IsOpenProperty, value);
        }

        // 装载具体弹窗内容的槽位
        public static readonly DependencyProperty DialogContentProperty = DependencyProperty.Register("DialogContent", typeof(object), typeof(DialogHost), new PropertyMetadata(null));

        public object DialogContent
        {
            get => GetValue(DialogContentProperty);
            set => SetValue(DialogContentProperty, value);
        }
    }
}
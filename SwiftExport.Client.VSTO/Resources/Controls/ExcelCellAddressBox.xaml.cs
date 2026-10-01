using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace SwiftExport.Client.VSTO.Resources.Controls
{
    /// <summary>紧凑的地址编辑与获取按钮，不访问 Excel COM，不管理配置业务。</summary>
    public partial class ExcelCellAddressBox : UserControl
    {
        /// <summary>提供双向绑定的 A1 地址，不解释业务字段。</summary>
        public static readonly DependencyProperty AddressProperty = DependencyProperty.Register(nameof(Address), typeof(string), typeof(ExcelCellAddressBox), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));
        /// <summary>由页面提供获取地址操作。</summary>
        public static readonly DependencyProperty PickCommandProperty = DependencyProperty.Register(nameof(PickCommand), typeof(ICommand), typeof(ExcelCellAddressBox));
        /// <summary>由页面提供清除地址操作。</summary>
        public static readonly DependencyProperty ClearCommandProperty = DependencyProperty.Register(nameof(ClearCommand), typeof(ICommand), typeof(ExcelCellAddressBox));
        /// <summary>传递当前字段模型，不把视图对象交给命令。</summary>
        public static readonly DependencyProperty CommandParameterProperty = DependencyProperty.Register(nameof(CommandParameter), typeof(object), typeof(ExcelCellAddressBox));
        /// <summary>装载地址编辑器，不请求配置或读取单元格。</summary>
        public ExcelCellAddressBox() { InitializeComponent(); }
        /// <summary>提供双向绑定的 A1 地址，不解释业务字段。</summary>
        public string Address { get => (string)GetValue(AddressProperty); set => SetValue(AddressProperty, value); }
        /// <summary>由页面提供获取地址操作。</summary>
        public ICommand PickCommand { get => (ICommand)GetValue(PickCommandProperty); set => SetValue(PickCommandProperty, value); }
        /// <summary>由页面提供清除地址操作。</summary>
        public ICommand ClearCommand { get => (ICommand)GetValue(ClearCommandProperty); set => SetValue(ClearCommandProperty, value); }
        /// <summary>传递当前字段模型，不把视图对象交给命令。</summary>
        public object CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
    }
}

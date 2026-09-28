using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExport.Client.VSTO.Resources.Converters
{
    /// <summary>为日期输入框预留日历按钮宽度；库内同名转换器不是公共 XAML 类型。</summary>
    public sealed class DatePickerButtonPaddingConverter : IMultiValueConverter
    {
        /// <summary>合并输入框 Padding、按钮 Margin 与按钮宽度，绑定未就绪时保留默认布局。</summary>
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length == 3 && values[0] is Thickness padding
                && values[1] is Thickness margin && values[2] is double width)
                return new Thickness(padding.Left, padding.Top, padding.Right + margin.Left + margin.Right + width, padding.Bottom);
            return DependencyProperty.UnsetValue;
        }

        /// <summary>布局计算只读，不把结果写回控件属性。</summary>
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return Array.ConvertAll(targetTypes, type => Binding.DoNothing);
        }
    }
}

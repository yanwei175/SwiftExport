using System;
using System.Collections;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.Client.VSTO.Resources.Converters
{
    /// <summary>安全读取原生 WPF 验证集合中的首条提示，不依赖 Grid 扩展框架。</summary>
    public sealed class FirstValidationErrorConverter : IValueConverter
    {
        /// <summary>返回首条验证提示；空集合或未初始化绑定返回空值。</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is IEnumerable errors)
            {
                foreach (var error in errors)
                    if (error is ValidationError validationError) return validationError.ErrorContent;
            }
            return null;
        }

        /// <summary>验证提示只读，不向错误集合写回。</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}

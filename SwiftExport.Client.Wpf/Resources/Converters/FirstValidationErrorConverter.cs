using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;

namespace SwiftExportClient.Resources.Converters;

/// <summary>安全读取首条原生验证提示；错误集合清空时返回空值，避免索引绑定越界。</summary>
public sealed class FirstValidationErrorConverter : IValueConverter
{
    /// <summary>从当前错误集合读取提示，不把空集合或未初始化绑定作为异常。</summary>
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is IEnumerable errors)
        {
            foreach (var error in errors)
                if (error is ValidationError validationError)
                    return validationError.ErrorContent;
        }
        return null;
    }

    /// <summary>验证提示只读，不向错误集合写回。</summary>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}

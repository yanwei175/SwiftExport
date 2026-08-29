using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters;

/// <summary>把带有 <see cref="DescriptionAttribute"/> 的共享枚举转换为面向用户的中文文本。</summary>
public sealed class EnumDescriptionConverter : IValueConverter
{
    /// <summary>优先返回枚举成员的 Description；未配置时回退到枚举成员名称。</summary>
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Enum enumValue) return value?.ToString() ?? string.Empty;
        var member = enumValue.GetType().GetField(enumValue.ToString());
        return member?.GetCustomAttributes(typeof(DescriptionAttribute), false)
                   .OfType<DescriptionAttribute>()
                   .FirstOrDefault()?.Description
               ?? enumValue.ToString();
    }

    /// <summary>枚举中文文本仅用于只读展示，不支持从中文文本反向写回枚举。</summary>
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

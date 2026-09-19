using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExportClient.Resources.Converters
{
    /// <summary>判断实数的正负：负数返回 -1，零返回 0，正数返回 1。</summary>
    public sealed class RealNumberSignConverter : IValueConverter
    {
        /// <summary>支持常用数值类型；空值、非数值和非有限浮点数不参与判断。</summary>
        public object Convert(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            return value switch
            {
                decimal number => Math.Sign(number),
                double number when double.IsFinite(number) => Math.Sign(number),
                float number when float.IsFinite(number) => Math.Sign(number),
                sbyte number => Math.Sign(number),
                short number => Math.Sign(number),
                int number => Math.Sign(number),
                long number => Math.Sign(number),
                byte number => number == 0 ? 0 : 1,
                ushort number => number == 0 ? 0 : 1,
                uint number => number == 0 ? 0 : 1,
                ulong number => number == 0 ? 0 : 1,
                _ => DependencyProperty.UnsetValue
            };
        }

        /// <summary>仅用于显示判断，不支持反向转换。</summary>
        public object ConvertBack(
            object? value,
            Type targetType,
            object? parameter,
            CultureInfo culture) => Binding.DoNothing;
    }
}

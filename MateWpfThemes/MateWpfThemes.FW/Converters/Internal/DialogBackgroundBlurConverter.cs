using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Effects;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据对话框状态决定是否对背景应用模糊效果。
    /// </summary>
    public sealed class DialogBackgroundBlurConverter : IMultiValueConverter
    {
        public static readonly DialogBackgroundBlurConverter Instance = new DialogBackgroundBlurConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 中文注释：兼容 C#7.3，使用传统类型检查代替列表模式匹配。
            if (values != null && values.Length >= 3)
            {
                if (values[0] is bool && values[1] is bool && values[2] is double)
                {
                    bool isOpen = (bool)values[0];
                    bool applyBlurBackground = (bool)values[1];
                    double blurRadius = (double)values[2];

                    if (isOpen && applyBlurBackground)
                    {
                        return new BlurEffect { Radius = blurRadius };
                    }
                }
            }

            return null;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

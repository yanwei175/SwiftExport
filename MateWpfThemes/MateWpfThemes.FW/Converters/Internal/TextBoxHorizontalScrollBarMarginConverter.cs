using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：计算 TextBox 横向滚动条的边距，使其避开前置图标/前缀文本。
    /// </summary>
    public class TextBoxHorizontalScrollBarMarginConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values != null && values.Length >= 9 &&
                values[0] is double &&
                values[1] is Thickness &&
                values[2] is double &&
                values[3] is Thickness &&
                values[4] is bool &&
                values[5] is bool &&
                values[6] is bool &&
                values[7] is Thickness &&
                values[8] is Thickness)
            {
                double leadingIconWidth = (double)values[0];
                Thickness leadingIconMargin = (Thickness)values[1];
                double prefixTextWidth = (double)values[2];
                Thickness prefixTextMargin = (Thickness)values[3];
                bool isMouseOver = (bool)values[4];
                bool hasKeyboardFocus = (bool)values[5];
                bool hasOutlinedTextField = (bool)values[6];
                Thickness normalBorder = (Thickness)values[7];
                Thickness activeBorder = (Thickness)values[8];

                double iconMargin = leadingIconWidth > 0 ? leadingIconMargin.Left + leadingIconMargin.Right : 0;
                double prefixMargin = prefixTextWidth > 0 ? prefixTextMargin.Left + prefixTextMargin.Right : 0;
                double offset = leadingIconWidth + iconMargin + prefixTextWidth + prefixMargin;
                double bottomOffset = 0;
                double topOffset = 0;

                if (hasOutlinedTextField && (isMouseOver || hasKeyboardFocus))
                {
                    double horizDelta = activeBorder.Left - normalBorder.Left;
                    double vertDeltaTop = activeBorder.Top - normalBorder.Top;
                    double vertDeltaBottom = activeBorder.Bottom - normalBorder.Bottom;
                    offset -= horizDelta;
                    topOffset += vertDeltaTop;
                    bottomOffset -= vertDeltaBottom;
                }

                return new Thickness(offset, topOffset, 0, bottomOffset);
            }

            return new Thickness(0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

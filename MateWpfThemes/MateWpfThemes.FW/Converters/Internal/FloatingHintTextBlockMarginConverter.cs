using MateWpfThemes.FW.Common;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据浮动提示与静止提示的对齐方式，计算提示文本的左侧偏移。
    /// </summary>
    public class FloatingHintTextBlockMarginConverter : IMultiValueConverter
    {
        public static readonly FloatingHintTextBlockMarginConverter Instance = new FloatingHintTextBlockMarginConverter();

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 中文注释：参数数量与类型校验，避免运行时异常。
            if (values == null || values.Length < 8)
            {
                return new Thickness(0);
            }

            if (!(values[0] is FloatingHintHorizontalAlignment) ||
                !(values[1] is FloatingHintHorizontalAlignment) ||
                !(values[2] is HorizontalAlignment) ||
                !(values[3] is double) ||
                !(values[4] is double) ||
                !(values[5] is double) ||
                !(values[6] is double) ||
                !(values[7] is double))
            {
                return new Thickness(0);
            }

            var restingAlignmentOverride = (FloatingHintHorizontalAlignment)values[0];
            var floatingAlignment = (FloatingHintHorizontalAlignment)values[1];
            var restingAlignment = (HorizontalAlignment)values[2];
            var desiredWidth = (double)values[3];
            var availableWidth = (double)values[4];
            var scale = (double)values[5];
            var lower = (double)values[6];
            var upper = (double)values[7];

            double scaleMultiplier = upper + (lower - upper) * scale;

            HorizontalAlignment restAlignment = ResolveHorizontalAlignment(restingAlignmentOverride, restingAlignment);
            HorizontalAlignment floatAlignment = restAlignment;

            if (scale != 0)
            {
                floatAlignment = ResolveHorizontalAlignment(floatingAlignment, restingAlignment);
            }

            double leftThickness;
            if (floatAlignment == HorizontalAlignment.Right)
            {
                leftThickness = FloatRight(restAlignment, desiredWidth, availableWidth, scaleMultiplier, scale);
            }
            else if (floatAlignment == HorizontalAlignment.Center)
            {
                leftThickness = FloatCenter(restAlignment, desiredWidth, availableWidth, scaleMultiplier, scale);
            }
            else
            {
                leftThickness = FloatLeft(restAlignment, desiredWidth, availableWidth, scale);
            }

            return new Thickness(Math.Round(leftThickness), 0, 0, 0);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 中文注释：把 FloatingHintHorizontalAlignment 映射为 WPF HorizontalAlignment。
        /// </summary>
        private static HorizontalAlignment ResolveHorizontalAlignment(FloatingHintHorizontalAlignment alignment, HorizontalAlignment fallback)
        {
            switch (alignment)
            {
                case FloatingHintHorizontalAlignment.Inherit:
                    return fallback;
                case FloatingHintHorizontalAlignment.Left:
                    return HorizontalAlignment.Left;
                case FloatingHintHorizontalAlignment.Center:
                    return HorizontalAlignment.Center;
                case FloatingHintHorizontalAlignment.Right:
                    return HorizontalAlignment.Right;
                case FloatingHintHorizontalAlignment.Stretch:
                    return HorizontalAlignment.Stretch;
                default:
                    throw new ArgumentOutOfRangeException(nameof(alignment));
            }
        }

        private static double FloatLeft(HorizontalAlignment restAlignment, double desiredWidth, double availableWidth, double scale)
        {
            if (restAlignment == HorizontalAlignment.Center)
            {
                double offset = Math.Max(0, (availableWidth - desiredWidth) / 2);
                return offset - offset * scale;
            }

            if (restAlignment == HorizontalAlignment.Right)
            {
                double offset = Math.Max(0, availableWidth - desiredWidth);
                return offset - offset * scale;
            }

            return 0;
        }

        private static double FloatCenter(HorizontalAlignment restAlignment, double desiredWidth, double availableWidth, double scaleMultiplier, double scale)
        {
            if (restAlignment == HorizontalAlignment.Left || restAlignment == HorizontalAlignment.Stretch)
            {
                double offset = Math.Max(0, (availableWidth - desiredWidth * scaleMultiplier) / 2);
                return offset * scale;
            }

            if (restAlignment == HorizontalAlignment.Right)
            {
                double startOffset = Math.Max(0, availableWidth - desiredWidth);
                double endOffset = Math.Max(0, (availableWidth - desiredWidth) / 2);
                double endOffsetDelta = startOffset - endOffset;
                return endOffset + endOffsetDelta * (1 - scale);
            }

            return Math.Max(0, availableWidth - desiredWidth * scaleMultiplier) / 2;
        }

        private static double FloatRight(HorizontalAlignment restAlignment, double desiredWidth, double availableWidth, double scaleMultiplier, double scale)
        {
            if (restAlignment == HorizontalAlignment.Left || restAlignment == HorizontalAlignment.Stretch)
            {
                double offset = Math.Max(0, availableWidth - desiredWidth * scaleMultiplier);
                return offset * scale;
            }

            if (restAlignment == HorizontalAlignment.Center)
            {
                double startOffset = Math.Max(0, (availableWidth - desiredWidth) / 2);
                double endOffsetDelta = Math.Max(0, availableWidth - desiredWidth * scaleMultiplier) - startOffset;
                return startOffset + endOffsetDelta * scale;
            }

            return Math.Max(0, availableWidth - desiredWidth * scaleMultiplier);
        }
    }
}

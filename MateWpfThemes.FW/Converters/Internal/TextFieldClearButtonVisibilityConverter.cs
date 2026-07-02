using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据控件状态计算清空按钮可见性。
    /// </summary>
    public class TextFieldClearButtonVisibilityConverter : IMultiValueConverter
    {
        public Visibility ContentEmptyVisibility { get; set; } = Visibility.Hidden;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2 || !(values[0] is bool) || !(values[1] is bool))
            {
                return Visibility.Visible;
            }

            bool hasClearButton = (bool)values[0];
            bool isContentNullOrEmpty = (bool)values[1];

            // TextFieldAssist.HasClearButton
            if (!hasClearButton)
            {
                return Visibility.Collapsed;
            }

            // ComboBox.IsEditable（当第三个值存在且为 false 时）
            if (isContentNullOrEmpty && values.Length > 2 && values[2] is bool && ((bool)values[2]) == false)
            {
                return Visibility.Collapsed;
            }

            // Hint.IsContentNullOrEmpty
            return isContentNullOrEmpty ? ContentEmptyVisibility : Visibility.Visible;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}

using System;
using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：判断 View 是否为且包含列的 GridView。
    /// </summary>
    internal class GridViewHasColumnsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var gridView = value as GridView;
            return gridView != null && gridView.Columns.Count > 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}

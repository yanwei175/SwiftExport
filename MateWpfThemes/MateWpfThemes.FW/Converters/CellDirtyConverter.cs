using MateWpfThemes.FW.Expand;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters
{
    public class CellDirtyConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // 防御性判断：处理 WPF 虚拟化时的断开连接状态
            if (values.Length < 2 || values[0] == null || values[0] == DependencyProperty.UnsetValue)
                return false;

            if (values[1] is DataGridCell cell && cell.Column is DataGridBoundColumn boundColumn && boundColumn.Binding is Binding binding)
            {
                return CellTrackerState.IsDirty(values[0], binding.Path.Path);
            }
            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotImplementedException();

    }
}

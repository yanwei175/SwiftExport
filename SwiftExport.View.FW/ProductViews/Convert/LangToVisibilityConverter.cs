using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace SwiftExport.View.FW.ProductViews.Convert
{
    public class LangToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            int index = (int)(value ?? 0);
            string type = parameter?.ToString();

            if (type == "En")
                return (index == 0 || index == 2) ? Visibility.Visible : Visibility.Collapsed;
            if (type == "Zh")
                return (index == 1 || index == 2) ? Visibility.Visible : Visibility.Collapsed;

            return Visibility.Visible;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
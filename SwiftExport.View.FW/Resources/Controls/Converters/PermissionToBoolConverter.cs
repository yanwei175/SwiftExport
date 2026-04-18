using SwiftExport.View.FW.Security;
using System;
using System.Globalization;
using System.Windows.Data;

namespace SwiftExport.View.FW.Resources.Controls.Converters
{
    public class PermissionToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string permissionCode = parameter as string;
            return PermissionManager.Instance.HasPermission(permissionCode);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
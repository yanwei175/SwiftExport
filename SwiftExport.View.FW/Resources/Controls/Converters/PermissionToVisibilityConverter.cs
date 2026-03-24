using SwiftExport.View.FW.Security;
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SwiftExport.View.FW.Resources.Controls.Converters
{
    public class PermissionToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // parameter 就是我们在 XAML 里写的权限码，比如 "Product:Delete"
            string permissionCode = parameter as string;

            // 调用全局管理器查权限
            bool hasPerm = PermissionManager.Instance.HasPermission(permissionCode);

            // 有权限就显示，没权限就折叠彻底消失 (Collapsed)
            return hasPerm ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

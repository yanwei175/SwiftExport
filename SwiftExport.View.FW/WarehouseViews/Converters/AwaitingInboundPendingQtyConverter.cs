using System;
using System.Globalization;
using System.Windows.Data;

namespace SwiftExport.View.FW.WarehouseViews.Converters
{
    /// <summary>
    /// 待收数量转换器：待收数 = QTY - RecQty。
    /// </summary>
    public class AwaitingInboundPendingQtyConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 0m;

            var qty = ToDecimal(values[0]);
            var recQty = ToDecimal(values[1]);
            var pending = qty - recQty;
            return pending < 0 ? 0m : pending;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            return null;
        }

        private static decimal ToDecimal(object value)
        {
            if (value == null) return 0m;
            try
            {
                return System.Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return 0m;
            }
        }
    }
}

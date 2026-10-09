using System;
using System.Globalization;
using System.Windows.Data;

namespace SwiftExport.Client.VSTO.Resources.Converters
{
    /// <summary>限制导入页单头区域最多占页面三成高度；窄窗体中使用局部滚动，为明细保留空间。</summary>
    public sealed class ImportHeaderMaxHeightConverter : IValueConverter
    {
        /// <summary>根据页面实际高度计算单头上限，首次布局尚无尺寸时采用 300 个设备无关单位。</summary>
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var height = value is double ? (double)value : 0;
            return height > 0 ? Math.Min(300, height * 0.3) : 300;
        }

        /// <summary>高度上限只从视图尺寸读取，不反向修改布局或业务模型。</summary>
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Binding.DoNothing;
        }
    }
}

using MateWpfThemes.FW.AttachedProperties;
using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;

namespace MateWpfThemes.FW.Converters.Internal
{
    /// <summary>
    /// 中文注释：根据 NavigationPanelPlacement 判断导航按钮可见性。
    /// </summary>
    public class TabControlNavButtonVisibilityConverter : IValueConverter
    {
        [TypeConverter(typeof(NavigationPanelPlacementTypeConverter))]
        public NavigationPanelPlacement[] VisiblePlacements { get; set; } = new NavigationPanelPlacement[0];

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is NavigationPanelPlacement)
            {
                var placement = (NavigationPanelPlacement)value;
                return VisiblePlacements.Contains(placement) ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// 中文注释：支持在 XAML 中用逗号分隔字符串配置 VisiblePlacements。
        /// </summary>
        private class NavigationPanelPlacementTypeConverter : TypeConverter
        {
            public override bool CanConvertFrom(ITypeDescriptorContext context, Type sourceType)
            {
                return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
            }

            public override object ConvertFrom(ITypeDescriptorContext context, CultureInfo culture, object value)
            {
                if (value is string)
                {
                    string s = (string)value;
                    return s.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(x => Enum.Parse(typeof(NavigationPanelPlacement), x.Trim()))
                            .Cast<NavigationPanelPlacement>()
                            .ToArray();
                }

                return base.ConvertFrom(context, culture, value);
            }
        }
    }
}

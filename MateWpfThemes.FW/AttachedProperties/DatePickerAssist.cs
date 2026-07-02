using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace MateWpfThemes.FW.AttachedProperties
{
    public static class DatePickerAssist
    {
        public static readonly DependencyProperty CornerRadiusProperty =
            DependencyProperty.RegisterAttached(
                "CornerRadius",
                typeof(CornerRadius),
                typeof(DatePickerAssist),
                new PropertyMetadata(new CornerRadius(4)));

        public static void SetCornerRadius(DependencyObject obj, CornerRadius value)
            => obj.SetValue(CornerRadiusProperty, value);

        public static CornerRadius GetCornerRadius(DependencyObject obj)
            => (CornerRadius)obj.GetValue(CornerRadiusProperty);
    }

}

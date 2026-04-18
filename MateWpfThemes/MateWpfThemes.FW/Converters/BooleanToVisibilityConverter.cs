using System.Windows;

namespace MateWpfThemes.FW.Converters
{
    public sealed class BooleanToVisibilityConverter : BooleanConverter<Visibility>
    {
        public static readonly BooleanToVisibilityConverter CollapsedInstance = new BooleanToVisibilityConverter() { FalseValue = Visibility.Collapsed, TrueValue = Visibility.Visible };
        public static readonly BooleanToVisibilityConverter NotCollapsedInstance = new BooleanToVisibilityConverter() { FalseValue = Visibility.Visible, TrueValue = Visibility.Collapsed };

        public static readonly BooleanToVisibilityConverter HiddenInstance = new BooleanToVisibilityConverter() { FalseValue = Visibility.Hidden, TrueValue = Visibility.Visible };
        public static readonly BooleanToVisibilityConverter NotHiddenInstance = new BooleanToVisibilityConverter() { FalseValue = Visibility.Visible, TrueValue = Visibility.Hidden };

        public BooleanToVisibilityConverter() :
            base(Visibility.Visible, Visibility.Collapsed)
        { }
    }
}

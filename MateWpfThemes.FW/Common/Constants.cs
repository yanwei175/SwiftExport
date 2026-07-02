using System.Windows;

namespace MateWpfThemes.FW.Common
{
    public static class Constants
    {
        public static readonly Thickness TextBoxDefaultPadding = new Thickness(0, 4, 0, 4);
        public static readonly Thickness FloatingTextBoxDefaultPadding = new Thickness(0, 12, 0, 4);
        public static readonly Thickness FilledTextBoxDefaultPadding = new Thickness(16, 16, 12, 8);
        public static readonly Thickness OutlinedTextBoxDefaultPadding = new Thickness(16, 16, 12, 16);
        public static readonly Thickness DefaultTextBoxViewMargin = new Thickness(1, 0, 1, 0);
        public static readonly Thickness DefaultTextBoxViewMarginStretch = new Thickness(1, 18, 1, 0);
        public static readonly Thickness DefaultTextBoxViewMarginEmbedded = new Thickness(0);
        public static readonly Thickness DefaultOutlinedBorderInactiveThickness = new Thickness(1);
        public static readonly Thickness DefaultOutlinedBorderActiveThickness = new Thickness(2);
        public const double TextBoxNotEnabledOpacity = 0.56;
        public const double ComboBoxArrowSize = 8;
        public const double InteractionHoverOpacity = 0.15;
    }
}

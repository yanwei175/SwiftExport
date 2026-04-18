using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MateWpfThemes.FW.AttachedProperties
{
    /// <summary>
    /// 验证提示辅助附加属性集合。
    /// 提供一组用于控制验证错误提示（Tooltip/Popup）显示行为和样式的附加属性。
    /// 这些属性通常在样式或控件模板中读取，用于统一控制验证提示的显示位置、偏移、外观等。
    /// </summary>
    public static class ValidationAssist
    {
        #region ShowOnFocusProperty指示是否仅在控件获得焦点时显示验证提示。

        /// <summary>
        /// 指示是否仅在控件获得焦点时显示验证提示。
        /// 默认值为 <c>false</c>（即无论是否有焦点均可显示）。
        /// </summary>
        public static readonly DependencyProperty OnlyShowOnFocusProperty = DependencyProperty.RegisterAttached(
            "OnlyShowOnFocus",
            typeof(bool),
            typeof(ValidationAssist),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// 获取 <see cref="OnlyShowOnFocusProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>如果仅在获取焦点时显示验证提示则为 <c>true</c>，否则为 <c>false</c>。</returns>
        public static bool GetOnlyShowOnFocus(DependencyObject element)
        {
            return (bool)element.GetValue(OnlyShowOnFocusProperty);
        }

        /// <summary>
        /// 设置 <see cref="OnlyShowOnFocusProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">要设置的布尔值。</param>
        public static void SetOnlyShowOnFocus(DependencyObject element, bool value)
        {
            element.SetValue(OnlyShowOnFocusProperty, value);
        }

        #endregion

        #region UsePopupProperty指示是否使用独立的 <see cref="Popup"/> 来显示验证提示（代替 Tooltip）。

        /// <summary>
        /// 指示是否使用独立的 <see cref="Popup"/> 来显示验证提示（代替 Tooltip）。
        /// 默认值为 <c>false</c>。
        /// 当需要更灵活的定位或自定义动画时，可将此值设为 <c>true</c>。
        /// </summary>
        public static readonly DependencyProperty UsePopupProperty = DependencyProperty.RegisterAttached(
            "UsePopup",
            typeof(bool),
            typeof(ValidationAssist),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// 获取 <see cref="UsePopupProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>如果使用 <see cref="Popup"/> 显示验证提示则为 <c>true</c>，否则为 <c>false</c>。</returns>
        public static bool GetUsePopup(DependencyObject element)
        {
            return (bool)element.GetValue(UsePopupProperty);
        }

        /// <summary>
        /// 设置 <see cref="UsePopupProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">要设置的布尔值。</param>
        public static void SetUsePopup(DependencyObject element, bool value)
        {
            element.SetValue(UsePopupProperty, value);
        }

        #endregion

        #region Popup弹出时候的位置和偏移控制属性
        /// <summary>
        /// 指定当使用 <see cref="Popup"/> 时的弹出位置（<see cref="PlacementMode"/>）。
        /// 默认值为 <see cref="PlacementMode.Bottom"/>。
        /// 该属性可继承，便于在容器级别统一设置。
        /// </summary>
        public static readonly DependencyProperty PopupPlacementProperty = DependencyProperty.RegisterAttached(
            "PopupPlacement",
            typeof(PlacementMode),
            typeof(ValidationAssist),
            new FrameworkPropertyMetadata(PlacementMode.Bottom, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// 获取 <see cref="PopupPlacementProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>当前的 <see cref="PlacementMode"/> 值。</returns>
        public static PlacementMode GetPopupPlacement(DependencyObject element)
        {
            return (PlacementMode)element.GetValue(PopupPlacementProperty);
        }

        /// <summary>
        /// 设置 <see cref="PopupPlacementProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">要设置的 <see cref="PlacementMode"/> 值。</param>
        public static void SetPopupPlacement(DependencyObject element, PlacementMode value)
        {
            element.SetValue(PopupPlacementProperty, value);
        }

        /// <summary>
        /// 中文注释：统一控制验证 Tooltip/Popup 的水平偏移。
        /// 默认不偏移，外部可按控件布局需要进行覆盖。
        /// </summary>
        public static readonly DependencyProperty PopupHorizontalOffsetProperty = DependencyProperty.RegisterAttached(
            "PopupHorizontalOffset",
            typeof(double),
            typeof(ValidationAssist),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// 获取 <see cref="PopupHorizontalOffsetProperty"/> 的值（以像素为单位）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>水平偏移量（像素）。</returns>
        public static double GetPopupHorizontalOffset(DependencyObject element)
        {
            return (double)element.GetValue(PopupHorizontalOffsetProperty);
        }

        /// <summary>
        /// 设置 <see cref="PopupHorizontalOffsetProperty"/> 的值（以像素为单位）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">水平偏移量（像素）。</param>
        public static void SetPopupHorizontalOffset(DependencyObject element, double value)
        {
            element.SetValue(PopupHorizontalOffsetProperty, value);
        }

        /// <summary>
        /// 中文注释：统一控制验证 Tooltip/Popup 的垂直偏移。
        /// 默认略微向下偏移，避免提示紧贴输入框边缘。
        /// </summary>
        public static readonly DependencyProperty PopupVerticalOffsetProperty = DependencyProperty.RegisterAttached(
            "PopupVerticalOffset",
            typeof(double),
            typeof(ValidationAssist),
            new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// 获取 <see cref="PopupVerticalOffsetProperty"/> 的值（以像素为单位）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>垂直偏移量（像素）。</returns>
        public static double GetPopupVerticalOffset(DependencyObject element)
        {
            return (double)element.GetValue(PopupVerticalOffsetProperty);
        }

        /// <summary>
        /// 设置 <see cref="PopupVerticalOffsetProperty"/> 的值（以像素为单位）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">垂直偏移量（像素）。</param>
        public static void SetPopupVerticalOffset(DependencyObject element, double value)
        {
            element.SetValue(PopupVerticalOffsetProperty, value);
        }
        #endregion


        #region SuppressProperty框架内部使用的开关属性，通常由样式或控件模板在运行时设置以抑制提示显示。

        /// <summary>
        /// Framework use only.
        /// 框架内部使用的开关属性，通常由样式或控件模板在运行时设置以抑制提示显示。
        /// 此属性可继承。
        /// </summary>
        public static readonly DependencyProperty SuppressProperty = DependencyProperty.RegisterAttached(
            "Suppress", typeof(bool), typeof(ValidationAssist), new FrameworkPropertyMetadata(default(bool), FrameworkPropertyMetadataOptions.Inherits));

        /// <summary>
        /// 设置 <see cref="SuppressProperty"/> 的值（框架内部使用）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">要设置的布尔值。</param>
        public static void SetSuppress(DependencyObject element, bool value)
        {
            element.SetValue(SuppressProperty, value);
        }

        /// <summary>
        /// 获取 <see cref="SuppressProperty"/> 的值（框架内部使用）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>如果已抑制提示显示则为 <c>true</c>，否则为 <c>false</c>。</returns>
        public static bool GetSuppress(DependencyObject element)
        {
            return (bool)element.GetValue(SuppressProperty);
        }
        #endregion


        #region BackgroundProperty用于自定义验证提示背景色的附加属性。
        /// <summary>
        /// 用于自定义验证提示背景色的附加属性（<see cref="Brush"/>）。
        /// 默认值为 <c>null</c>（使用控件样式默认背景）。
        /// </summary>
        public static readonly DependencyProperty BackgroundProperty = DependencyProperty.RegisterAttached(
            "Background", typeof(Brush), typeof(ValidationAssist), new PropertyMetadata(default(Brush)));

        /// <summary>
        /// 设置验证提示的背景画刷。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">背景画刷。</param>
        public static void SetBackground(DependencyObject element, Brush value)
        {
            element.SetValue(BackgroundProperty, value);
        }

        /// <summary>
        /// 获取验证提示的背景画刷。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>当前背景画刷，若未设置则返回 <c>null</c>。</returns>
        public static Brush GetBackground(DependencyObject element)
        {
            return (Brush)element.GetValue(BackgroundProperty);
        }

        #endregion


        #region FontSizeProperty用于控制验证提示字体大小的附加属性。

        /// <summary>
        /// 用于控制验证提示字体大小的附加属性（像素）。
        /// 默认值为 10.0。
        /// </summary>
        public static readonly DependencyProperty FontSizeProperty = DependencyProperty.RegisterAttached("FontSize", typeof(double), typeof(ValidationAssist), new PropertyMetadata(10.0));

        /// <summary>
        /// 设置验证提示的字体大小（像素）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">字体大小（像素）。</param>
        public static void SetFontSize(DependencyObject element, double value)
        {
            element.SetValue(FontSizeProperty, value);
        }

        /// <summary>
        /// 获取验证提示的字体大小（像素）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>字体大小（像素）。</returns>
        public static double GetFontSize(DependencyObject element)
        {
            return (double)element.GetValue(FontSizeProperty);
        }
        #endregion


        #region HasErrorProperty指示控件是否处于验证错误状态的附加属性，供样式或触发器使用。
        /// <summary>
        /// 指示控件是否处于验证错误状态（只作标记用途，默认 <c>false</c>）。
        /// 样式或触发器可基于该属性改变显示（例如改变边框、显示提示等）。
        /// </summary>
        public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
            "HasError",
            typeof(bool),
            typeof(ValidationAssist),
            new PropertyMetadata(default(bool)));

        /// <summary>
        /// 设置 <see cref="HasErrorProperty"/>（表示是否存在验证错误）。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">是否存在错误。</param>
        public static void SetHasError(DependencyObject element, bool value)
        {
            element.SetValue(HasErrorProperty, value);
        }

        /// <summary>
        /// 获取 <see cref="HasErrorProperty"/> 的值。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>若存在验证错误则为 <c>true</c>，否则为 <c>false</c>。</returns>
        public static bool GetHasError(DependencyObject element)
        {
            return (bool)element.GetValue(HasErrorProperty);
        }
        #endregion


        #region HorizontalAlignment用于控制验证提示水平对齐方式的附加属性。
        /// <summary>
        /// 控制验证提示的水平对齐方式（相对于参照控件），默认 <see cref="HorizontalAlignment.Left"/>。
        /// </summary>
        public static readonly DependencyProperty HorizontalAlignmentProperty = DependencyProperty.RegisterAttached(
            "HorizontalAlignment", typeof(HorizontalAlignment), typeof(ValidationAssist), new PropertyMetadata(HorizontalAlignment.Left));

        /// <summary>
        /// 设置水平对齐方式。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <param name="value">水平对齐方式。</param>
        public static void SetHorizontalAlignment(DependencyObject element, HorizontalAlignment value) => element.SetValue(HorizontalAlignmentProperty, value);

        /// <summary>
        /// 获取水平对齐方式。
        /// </summary>
        /// <param name="element">目标 <see cref="DependencyObject"/>。</param>
        /// <returns>当前的 <see cref="HorizontalAlignment"/>。</returns>
        public static HorizontalAlignment GetHorizontalAlignment(DependencyObject element) => (HorizontalAlignment)element.GetValue(HorizontalAlignmentProperty);
        #endregion
    }
}
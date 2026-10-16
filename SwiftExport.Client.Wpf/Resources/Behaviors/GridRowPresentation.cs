using System.Windows;
using System.Windows.Media;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>
/// 与业务状态无关的行视觉槽。宿主通过 Binding、Converter 或 Style Trigger 映射既有 RowState。
/// </summary>
public static class GridRowPresentation
{
    /// <summary>行头状态标记画刷；该值向行内视觉元素继承。</summary>
    public static readonly DependencyProperty MarkerBrushProperty = DependencyProperty.RegisterAttached(
        "MarkerBrush",
        typeof(Brush),
        typeof(GridRowPresentation),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>状态的可访问文本；该值向行内视觉元素继承。</summary>
    public static readonly DependencyProperty MarkerTextProperty = DependencyProperty.RegisterAttached(
        "MarkerText",
        typeof(string),
        typeof(GridRowPresentation),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>获取行头状态标记画刷。</summary>
    public static Brush? GetMarkerBrush(DependencyObject element) =>
        (Brush?)element.GetValue(MarkerBrushProperty);

    /// <summary>设置行头状态标记画刷。</summary>
    public static void SetMarkerBrush(DependencyObject element, Brush? value) =>
        element.SetValue(MarkerBrushProperty, value);

    /// <summary>获取状态的可访问文本。</summary>
    public static string? GetMarkerText(DependencyObject element) =>
        (string?)element.GetValue(MarkerTextProperty);

    /// <summary>设置状态的可访问文本。</summary>
    public static void SetMarkerText(DependencyObject element, string? value) =>
        element.SetValue(MarkerTextProperty, value);
}

using System.Windows;
using System.Windows.Controls;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Editing;

/// <summary>
/// 为原生 <see cref="DataGrid"/> 提供可独立启停的企业级编辑键盘约定。
/// </summary>
public static class GridEditing
{
    /// <summary>标识是否启用编辑增强。默认关闭，关闭后完全沿用原生 DataGrid 行为。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridEditing),
        new PropertyMetadata(false));

    /// <summary>标识双击当前可编辑单元格时是否开始编辑。默认启用。</summary>
    public static readonly DependencyProperty BeginEditOnDoubleClickProperty = DependencyProperty.RegisterAttached(
        "BeginEditOnDoubleClick",
        typeof(bool),
        typeof(GridEditing),
        new PropertyMetadata(true));

    /// <summary>标识 F2 是否开始编辑当前可编辑单元格。默认启用。</summary>
    public static readonly DependencyProperty BeginEditOnF2Property = DependencyProperty.RegisterAttached(
        "BeginEditOnF2",
        typeof(bool),
        typeof(GridEditing),
        new PropertyMetadata(true));

    /// <summary>配置 Enter 的编辑行为。</summary>
    public static readonly DependencyProperty EnterKeyModeProperty = DependencyProperty.RegisterAttached(
        "EnterKeyMode",
        typeof(GridEnterKeyMode),
        typeof(GridEditing),
        new PropertyMetadata(GridEnterKeyMode.BeginEditOrCommitStay));

    /// <summary>配置 Tab/Shift+Tab 的单元格导航行为。</summary>
    public static readonly DependencyProperty TabNavigationModeProperty = DependencyProperty.RegisterAttached(
        "TabNavigationMode",
        typeof(GridTabNavigationMode),
        typeof(GridEditing),
        new PropertyMetadata(GridTabNavigationMode.EditableCells));

    /// <summary>获取是否启用编辑增强。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用编辑增强。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>获取是否允许双击开始编辑。</summary>
    public static bool GetBeginEditOnDoubleClick(DependencyObject element) =>
        (bool)element.GetValue(BeginEditOnDoubleClickProperty);

    /// <summary>设置是否允许双击开始编辑。</summary>
    public static void SetBeginEditOnDoubleClick(DependencyObject element, bool value) =>
        element.SetValue(BeginEditOnDoubleClickProperty, value);

    /// <summary>获取是否允许 F2 开始编辑。</summary>
    public static bool GetBeginEditOnF2(DependencyObject element) =>
        (bool)element.GetValue(BeginEditOnF2Property);

    /// <summary>设置是否允许 F2 开始编辑。</summary>
    public static void SetBeginEditOnF2(DependencyObject element, bool value) =>
        element.SetValue(BeginEditOnF2Property, value);

    /// <summary>获取 Enter 行为。</summary>
    public static GridEnterKeyMode GetEnterKeyMode(DependencyObject element) =>
        (GridEnterKeyMode)element.GetValue(EnterKeyModeProperty);

    /// <summary>设置 Enter 行为。</summary>
    public static void SetEnterKeyMode(DependencyObject element, GridEnterKeyMode value) =>
        element.SetValue(EnterKeyModeProperty, value);

    /// <summary>获取 Tab 导航行为。</summary>
    public static GridTabNavigationMode GetTabNavigationMode(DependencyObject element) =>
        (GridTabNavigationMode)element.GetValue(TabNavigationModeProperty);

    /// <summary>设置 Tab 导航行为。</summary>
    public static void SetTabNavigationMode(DependencyObject element, GridTabNavigationMode value) =>
        element.SetValue(TabNavigationModeProperty, value);
}

/// <summary>Enter 键在 Enterprise Grid 中的处理模式。</summary>
public enum GridEnterKeyMode
{
    /// <summary>不增强 Enter，完全交由原生控件或编辑器处理。</summary>
    Native,

    /// <summary>空闲时开始编辑；编辑时提交当前 Cell 并停留。</summary>
    BeginEditOrCommitStay
}

/// <summary>Tab/Shift+Tab 在 Enterprise Grid 中的处理模式。</summary>
public enum GridTabNavigationMode
{
    /// <summary>不增强 Tab，完全沿用原生 DataGrid 行为。</summary>
    Native,

    /// <summary>提交成功后按 DisplayIndex 移动到下一个或上一个可编辑 Cell。</summary>
    EditableCells
}

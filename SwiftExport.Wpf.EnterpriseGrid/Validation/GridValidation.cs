using System.Windows;
using System.Windows.Controls;
using EnterpriseGridCore = SwiftExport.Wpf.EnterpriseGrid.Core.EnterpriseGrid;

namespace SwiftExport.Wpf.EnterpriseGrid.Validation;

/// <summary>
/// 聚合原生 WPF 验证来源并提供 MVVM 可绑定的错误摘要与首错导航。
/// </summary>
public static class GridValidation
{
    /// <summary>标识是否启用验证聚合。默认关闭；不影响 DataGrid 原生验证本身。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridValidation),
        new PropertyMetadata(false, OnIsEnabledChanged));

    private static readonly DependencyPropertyKey ErrorsPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Errors",
        typeof(IReadOnlyList<GridValidationError>),
        typeof(GridValidation),
        new PropertyMetadata(Array.Empty<GridValidationError>()));

    /// <summary>按当前视图行序和列 DisplayIndex 排序的只读错误列表。</summary>
    public static readonly DependencyProperty ErrorsProperty = ErrorsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ErrorCountPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "ErrorCount",
        typeof(int),
        typeof(GridValidation),
        new PropertyMetadata(0));

    /// <summary>当前聚合错误数。</summary>
    public static readonly DependencyProperty ErrorCountProperty = ErrorCountPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey HasErrorsPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "HasErrors",
        typeof(bool),
        typeof(GridValidation),
        new PropertyMetadata(false));

    /// <summary>当前是否存在验证错误。</summary>
    public static readonly DependencyProperty HasErrorsProperty = HasErrorsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey FirstErrorPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "FirstError",
        typeof(GridValidationError),
        typeof(GridValidation),
        new PropertyMetadata(null));

    /// <summary>当前视图顺序中的首个错误。</summary>
    public static readonly DependencyProperty FirstErrorProperty = FirstErrorPropertyKey.DependencyProperty;

    /// <summary>获取是否启用验证聚合。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用验证聚合。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>获取只读错误列表。</summary>
    public static IReadOnlyList<GridValidationError> GetErrors(DependencyObject element) =>
        (IReadOnlyList<GridValidationError>)element.GetValue(ErrorsProperty);

    /// <summary>获取错误数。</summary>
    public static int GetErrorCount(DependencyObject element) => (int)element.GetValue(ErrorCountProperty);

    /// <summary>获取是否存在错误。</summary>
    public static bool GetHasErrors(DependencyObject element) => (bool)element.GetValue(HasErrorsProperty);

    /// <summary>获取当前首错。</summary>
    public static GridValidationError? GetFirstError(DependencyObject element) =>
        (GridValidationError?)element.GetValue(FirstErrorProperty);

    /// <summary>
    /// 将原生 DataGrid 导航到当前首错。仅定位数据项与 ColumnKey，不缓存虚拟化容器。
    /// </summary>
    public static bool FocusFirstError(DataGrid dataGrid) =>
        EnterpriseGridCore.GetCoordinator(dataGrid)?.Validation.FocusFirstError() == true;

    internal static void SetErrorsFromView(DataGrid dataGrid, IReadOnlyList<GridValidationError> errors)
    {
        dataGrid.SetValue(ErrorsPropertyKey, errors);
        dataGrid.SetValue(ErrorCountPropertyKey, errors.Count);
        dataGrid.SetValue(HasErrorsPropertyKey, errors.Count > 0);
        dataGrid.SetValue(FirstErrorPropertyKey, errors.Count > 0 ? errors[0] : null);
    }

    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is DataGrid dataGrid)
        {
            EnterpriseGridCore.GetCoordinator(dataGrid)?.Validation.RefreshEnabledState();
        }
    }
}

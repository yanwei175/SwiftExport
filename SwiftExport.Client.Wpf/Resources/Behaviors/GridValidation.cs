using SwiftExportClient.Resources.Behaviors.Internal;
using System.Windows;
using System.Windows.Controls;

namespace SwiftExportClient.Resources.Behaviors;

/// <summary>
/// 合并模型软错误通知并提供样式提示；原生转换错误不进入此快照。
/// </summary>
public static class GridValidation
{
    /// <summary>标识是否启用验证聚合。默认关闭；不影响 DataGrid 原生验证本身。</summary>
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled",
        typeof(bool),
        typeof(GridValidation),
        new PropertyMetadata(false, OnIsEnabledChanged));

    /// <summary>模板列软验证对应的模型字段；原生绑定列直接读取 Binding.Path。</summary>
    public static readonly DependencyProperty FieldPathProperty = DependencyProperty.RegisterAttached(
        "FieldPath", typeof(string), typeof(GridValidation));

    /// <summary>读取模板列的软验证字段映射。</summary>
    public static string? GetFieldPath(DependencyObject element) => (string?)element.GetValue(FieldPathProperty);

    /// <summary>设置模板列的软验证字段映射，不用于复制或业务赋值。</summary>
    public static void SetFieldPath(DependencyObject element, string? value) => element.SetValue(FieldPathProperty, value);

    private static readonly DependencyPropertyKey ErrorsPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Errors",
        typeof(IReadOnlyList<GridValidationError>),
        typeof(GridValidation),
        new FrameworkPropertyMetadata(
            Array.Empty<GridValidationError>(),
            FrameworkPropertyMetadataOptions.Inherits));

    /// <summary>调度合并后发布的模型软错误快照，不承诺排序，也不用于保存校验。</summary>
    public static readonly DependencyProperty ErrorsProperty = ErrorsPropertyKey.DependencyProperty;

    private static readonly DependencyPropertyKey ErrorCountPropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "ErrorCount",
        typeof(int),
        typeof(GridValidation),
        new PropertyMetadata(0));

    /// <summary>当前模型软错误数，不包含编辑器原生转换错误。</summary>
    public static readonly DependencyProperty ErrorCountProperty = ErrorCountPropertyKey.DependencyProperty;

    /// <summary>
    /// 由宿主 CellStyle 绑定的非阻断错误状态。该属性只负责呈现，不参与 DataGrid 提交事务。
    /// </summary>
    public static readonly DependencyProperty CellStateProperty = DependencyProperty.RegisterAttached(
        "CellState",
        typeof(GridCellValidationState),
        typeof(GridValidation),
        new PropertyMetadata(GridCellValidationState.Empty));

    /// <summary>由宿主 RowStyle 绑定的非阻断行错误状态。</summary>
    public static readonly DependencyProperty RowStateProperty = DependencyProperty.RegisterAttached(
        "RowState",
        typeof(GridCellValidationState),
        typeof(GridValidation),
        new PropertyMetadata(GridCellValidationState.Empty));

    /// <summary>获取是否启用验证聚合。</summary>
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    /// <summary>设置是否启用验证聚合。</summary>
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    /// <summary>获取只读错误列表。</summary>
    public static IReadOnlyList<GridValidationError> GetErrors(DependencyObject element) =>
        (IReadOnlyList<GridValidationError>)element.GetValue(ErrorsProperty);

    /// <summary>获取错误数。</summary>
    public static int GetErrorCount(DependencyObject element) => (int)element.GetValue(ErrorCountProperty);

    /// <summary>获取宿主样式计算出的当前 Cell 软验证状态。</summary>
    public static GridCellValidationState GetCellState(DependencyObject element) =>
        (GridCellValidationState)element.GetValue(CellStateProperty);

    /// <summary>设置当前 Cell 的软验证状态，通常由 MultiBinding 转换器调用。</summary>
    public static void SetCellState(DependencyObject element, GridCellValidationState value) =>
        element.SetValue(CellStateProperty, value ?? GridCellValidationState.Empty);

    /// <summary>获取宿主样式计算出的当前行软验证状态。</summary>
    public static GridCellValidationState GetRowState(DependencyObject element) =>
        (GridCellValidationState)element.GetValue(RowStateProperty);

    /// <summary>设置当前行软验证状态，通常由 MultiBinding 转换器调用。</summary>
    public static void SetRowState(DependencyObject element, GridCellValidationState value) =>
        element.SetValue(RowStateProperty, value ?? GridCellValidationState.Empty);

    internal static void SetErrorsFromView(DataGrid dataGrid, IReadOnlyList<GridValidationError> errors)
    {
        dataGrid.SetValue(ErrorsPropertyKey, errors);
        dataGrid.SetValue(ErrorCountPropertyKey, errors.Count);
    }

    private static readonly DependencyProperty ModuleProperty = DependencyProperty.RegisterAttached(
        "Module", typeof(GridValidationModule), typeof(GridValidation));

    /// <summary>仅在当前能力启用时持有适配实例；关闭即释放宿主与模型订阅。</summary>
    private static void OnIsEnabledChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        if (dependencyObject is not DataGrid dataGrid) return;
        var module = (GridValidationModule?)dataGrid.GetValue(ModuleProperty);
        if (GetIsEnabled(dataGrid))
        {
            if (module is null)
            {
                module = new GridValidationModule(dataGrid);
                dataGrid.SetValue(ModuleProperty, module);
                module.Attach();
            }
            else module.RefreshEnabledState();
        }
        else if (module is not null)
        {
            module.Detach();
            dataGrid.ClearValue(ModuleProperty);
        }
    }
}

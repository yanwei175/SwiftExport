using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SwiftExport.Wpf.EnterpriseGrid.Columns.Numeric;

/// <summary>
/// 基于原生 <see cref="DataGridTextColumn"/> 的数值便利列；允许完整输入并在原生 Cell 提交点验证。
/// </summary>
public class NumericDataGridColumn : DataGridTextColumn
{
    /// <summary>允许的最小值；为空表示不限制。</summary>
    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum),
        typeof(decimal?),
        typeof(NumericDataGridColumn),
        new PropertyMetadata(null));

    /// <summary>允许的最大值；为空表示不限制。</summary>
    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum),
        typeof(decimal?),
        typeof(NumericDataGridColumn),
        new PropertyMetadata(null));

    /// <summary>是否允许空文本。默认允许，以便业务 Binding/ValidationRule 决定必填语义。</summary>
    public static readonly DependencyProperty AllowEmptyProperty = DependencyProperty.Register(
        nameof(AllowEmpty),
        typeof(bool),
        typeof(NumericDataGridColumn),
        new PropertyMetadata(true));

    /// <summary>最大小数位数；-1 表示不限制。</summary>
    public static readonly DependencyProperty MaximumDecimalPlacesProperty = DependencyProperty.Register(
        nameof(MaximumDecimalPlaces),
        typeof(int),
        typeof(NumericDataGridColumn),
        new PropertyMetadata(-1),
        value => (int)value >= -1);

    private static readonly DependencyProperty OwnedValidationErrorProperty = DependencyProperty.RegisterAttached(
        "OwnedValidationError",
        typeof(System.Windows.Controls.ValidationError),
        typeof(NumericDataGridColumn));

    private static readonly NumericValidationRule ValidationRule = new();

    /// <summary>获取或设置允许的最小值。</summary>
    public decimal? Minimum
    {
        get => (decimal?)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>获取或设置允许的最大值。</summary>
    public decimal? Maximum
    {
        get => (decimal?)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>获取或设置是否允许空文本。</summary>
    public bool AllowEmpty
    {
        get => (bool)GetValue(AllowEmptyProperty);
        set => SetValue(AllowEmptyProperty, value);
    }

    /// <summary>获取或设置最大小数位数；-1 表示不限制。</summary>
    public int MaximumDecimalPlaces
    {
        get => (int)GetValue(MaximumDecimalPlacesProperty);
        set => SetValue(MaximumDecimalPlacesProperty, value);
    }

    /// <inheritdoc />
    protected override object PrepareCellForEdit(FrameworkElement editingElement, RoutedEventArgs editingEventArgs)
    {
        var original = base.PrepareCellForEdit(editingElement, editingEventArgs);
        if (editingElement is TextBox textBox)
        {
            textBox.SelectAll();
        }

        return original;
    }

    /// <inheritdoc />
    protected override bool CommitCellEdit(FrameworkElement editingElement)
    {
        if (editingElement is TextBox textBox && !TryValidate(textBox, out var message))
        {
            MarkInvalid(textBox, message);
            return false;
        }

        if (editingElement is TextBox validTextBox)
        {
            ClearOwnedError(validTextBox);
            if (validTextBox.GetBindingExpression(TextBox.TextProperty) is { } expression)
            {
                expression.UpdateSource();
                if (System.Windows.Controls.Validation.GetHasError(validTextBox))
                {
                    return false;
                }
            }
        }

        return base.CommitCellEdit(editingElement);
    }

    /// <inheritdoc />
    protected override void CancelCellEdit(FrameworkElement editingElement, object uneditedValue)
    {
        if (editingElement is TextBox textBox)
        {
            ClearOwnedError(textBox);
        }

        base.CancelCellEdit(editingElement, uneditedValue);
    }

    private bool TryValidate(TextBox textBox, out string message)
    {
        var text = textBox.Text?.Trim();
        if (string.IsNullOrEmpty(text))
        {
            message = "请输入数值。";
            return AllowEmpty;
        }

        CultureInfo culture;
        try
        {
            culture = textBox.Language.GetSpecificCulture();
        }
        catch (InvalidOperationException)
        {
            culture = CultureInfo.CurrentCulture;
        }

        if (!decimal.TryParse(text, NumberStyles.Number, culture, out var value))
        {
            message = "请输入有效数值。";
            return false;
        }

        if (Minimum is { } minimum && value < minimum)
        {
            message = $"数值不能小于 {minimum.ToString(culture)}。";
            return false;
        }

        if (Maximum is { } maximum && value > maximum)
        {
            message = $"数值不能大于 {maximum.ToString(culture)}。";
            return false;
        }

        if (MaximumDecimalPlaces >= 0 && GetDecimalPlaces(text, culture) > MaximumDecimalPlaces)
        {
            message = $"最多允许 {MaximumDecimalPlaces} 位小数。";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static int GetDecimalPlaces(string text, CultureInfo culture)
    {
        var separator = culture.NumberFormat.NumberDecimalSeparator;
        var separatorIndex = text.LastIndexOf(separator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            return 0;
        }

        var exponentIndex = text.IndexOfAny(['e', 'E'], separatorIndex + separator.Length);
        var endIndex = exponentIndex < 0 ? text.Length : exponentIndex;
        return Math.Max(0, endIndex - separatorIndex - separator.Length);
    }

    private static void MarkInvalid(TextBox textBox, string message)
    {
        if (textBox.GetBindingExpression(TextBox.TextProperty) is not { } expression)
        {
            return;
        }

        ClearOwnedError(textBox);
        var error = new System.Windows.Controls.ValidationError(ValidationRule, expression, message, null);
        textBox.SetValue(OwnedValidationErrorProperty, error);
        System.Windows.Controls.Validation.MarkInvalid(expression, error);
    }

    private static void ClearOwnedError(TextBox textBox)
    {
        if (textBox.GetValue(OwnedValidationErrorProperty) is not System.Windows.Controls.ValidationError error)
        {
            return;
        }

        if (textBox.GetBindingExpression(TextBox.TextProperty) is { } expression &&
            System.Windows.Controls.Validation.GetErrors(textBox).Contains(error))
        {
            System.Windows.Controls.Validation.ClearInvalid(expression);
        }

        textBox.ClearValue(OwnedValidationErrorProperty);
    }

    private sealed class NumericValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo) =>
            ValidationResult.ValidResult;
    }
}

using System.Windows;
using System.Windows.Controls;
using System.Globalization;

namespace SwiftExportClient.Resources.Controls;

/// <summary>
/// 保留无法解析的日期原文，由绑定模型提供软验证；合法日期仍使用原生 DatePicker 行为。
/// </summary>
public class SoftInputDatePicker : DatePicker
{
    public static readonly DependencyProperty RawTextProperty = DependencyProperty.Register(
        nameof(RawText),
        typeof(string),
        typeof(SoftInputDatePicker),
        new FrameworkPropertyMetadata(
            string.Empty,
            FrameworkPropertyMetadataOptions.BindsTwoWayByDefault,
            OnRawTextChanged));

    private bool _synchronizingRawText;

    public string RawText
    {
        get => (string)GetValue(RawTextProperty);
        set => SetValue(RawTextProperty, value);
    }

    protected override void OnDateValidationError(DatePickerDateValidationErrorEventArgs e)
    {
        e.ThrowException = false;
        SetRawText(e.Text);
        base.OnDateValidationError(e);
    }

    protected override void OnSelectedDateChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectedDateChanged(e);
        SetRawText(SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.CurrentCulture) ?? string.Empty);
    }

    private static void OnRawTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var datePicker = (SoftInputDatePicker)d;
        if (datePicker._synchronizingRawText)
        {
            return;
        }

        datePicker.SetCurrentValue(TextProperty, e.NewValue as string ?? string.Empty);
    }

    private void SetRawText(string? value)
    {
        _synchronizingRawText = true;
        try
        {
            SetCurrentValue(RawTextProperty, value ?? string.Empty);
            GetBindingExpression(RawTextProperty)?.UpdateSource();
        }
        finally
        {
            _synchronizingRawText = false;
        }
    }
}

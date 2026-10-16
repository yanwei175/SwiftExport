using System.Globalization;
using System.Windows.Controls;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.Views;

/// <summary>在原生文本列提交前校验折扣率输入，保留空值、非负和最多六位小数的原有边界。</summary>
public sealed class DiscountRateValidationRule : ValidationRule
{
    /// <summary>按绑定文化检查原始文本；失败时由 WPF 保留输入并阻止无效值写入行模型。</summary>
    public override ValidationResult Validate(object value, CultureInfo cultureInfo)
    {
        var text = (value as string)?.Trim();
        if (string.IsNullOrEmpty(text)) return ValidationResult.ValidResult;
        if (!decimal.TryParse(text, NumberStyles.Number, cultureInfo, out var number))
            return new ValidationResult(false, "请输入有效数值。");
        if (number < 0m) return new ValidationResult(false, "默认折扣率不能小于 0。");

        // 与退役 Numeric 列一致，按输入文本的小数位数检查，避免提交时静默截断。
        var separator = cultureInfo.NumberFormat.NumberDecimalSeparator;
        var position = text.LastIndexOf(separator, StringComparison.Ordinal);
        return position >= 0 && text.Length - position - separator.Length > 6
            ? new ValidationResult(false, "默认折扣率最多保留 6 位小数。")
            : ValidationResult.ValidResult;
    }
}

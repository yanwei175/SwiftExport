using System.Globalization;
using System.Windows.Controls;

namespace SwiftExportClient.Features.Customers.CustomerPoConfigs.Views;

/// <summary>在原生文本列提交前校验空值或 numeric(9,6) 范围，允许大于 1 的涨价倍率。</summary>
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
        if (number > 999.999999m) return new ValidationResult(false, "默认折扣率不能超过 999.999999。");

        // 按输入文本的小数位数检查，避免提交时静默截断或四舍五入。
        var separator = cultureInfo.NumberFormat.NumberDecimalSeparator;
        var position = text.LastIndexOf(separator, StringComparison.Ordinal);
        return position >= 0 && text.Length - position - separator.Length > 6
            ? new ValidationResult(false, "默认折扣率最多保留 6 位小数。")
            : ValidationResult.ValidResult;
    }
}

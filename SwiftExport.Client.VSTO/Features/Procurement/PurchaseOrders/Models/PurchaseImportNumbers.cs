using System;
using System.Globalization;
namespace SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models
{
    /// <summary>采购输入的数值解析与 SQL 精度边界；空和非法输入均不转成零。</summary>
    internal static class PurchaseImportNumbers
    {
        internal static decimal? Parse(string text) => decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out var value) ? (decimal?)value : null;
        internal static string Error(string text, string label, bool required, bool nonNegative, int scale, decimal maximum)
        {
            if (string.IsNullOrWhiteSpace(text)) return required ? label + "不能为空。" : "";
            var value = Parse(text);
            return !value.HasValue || nonNegative && value < 0 || value > maximum || value < -maximum
                || decimal.Round(value.Value, scale) != value ? label + "数值或精度无效。" : "";
        }
        internal static decimal? Amount(decimal? quantity, decimal? price)
        {
            if (!quantity.HasValue || !price.HasValue) return null;
            try { return checked(quantity * price); } catch (OverflowException) { return null; }
        }
    }
}


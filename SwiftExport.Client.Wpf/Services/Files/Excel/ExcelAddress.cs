using System.Text.RegularExpressions;

namespace SwiftExportClient.Services.Files.Excel;

/// <summary>验证单格 A1 地址并统一来源位置，不接受范围、工作表前缀或外部引用。</summary>
internal static class ExcelAddress
{
    internal const int MaximumRows = 1_048_576;
    internal const int MaximumColumns = 16_384;

    /// <summary>解析单格地址，保留一基行列；失败不产生可用地址。</summary>
    internal static bool TryParseCell(string? address, out int row, out int column)
    {
        row = column = 0;
        if (address is null || address.Length > 16)
            return false;
        var match = Regex.Match(address, @"\A\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})\z", RegexOptions.CultureInvariant);
        if (!match.Success || !int.TryParse(match.Groups[2].Value, out row)
            || !TryParseColumn(match.Groups[1].Value, out column))
            return false;
        return row <= MaximumRows;
    }

    /// <summary>解析列字母，拒绝单格地址及 Excel 范围以外的列。</summary>
    internal static bool TryParseColumn(string? letters, out int column)
    {
        column = 0;
        if (letters is null || letters.Length is < 1 or > 3)
            return false;
        foreach (var letter in letters)
        {
            var upper = char.ToUpperInvariant(letter);
            if (upper is < 'A' or > 'Z')
                return false;
            column = column * 26 + upper - 'A' + 1;
        }
        return column <= MaximumColumns;
    }

    /// <summary>从已验证的一基行列生成不带绝对标记的 A1 地址。</summary>
    internal static string Format(int row, int column)
    {
        var letters = string.Empty;
        while (column > 0)
        {
            column--;
            letters = (char)('A' + column % 26) + letters;
            column /= 26;
        }
        return letters + row.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}

using System.IO;
using System.Text;

namespace SwiftExport.Wpf.EnterpriseGrid.Exporting;

/// <summary>零第三方依赖、流式写入的 CSV 导出提供器。</summary>
public sealed class CsvGridExportProvider : IGridExportProvider
{
    /// <summary>初始化 CSV 提供器。</summary>
    /// <param name="delimiter">字段分隔符。</param>
    /// <param name="encoding">文本编码；默认带 BOM 的 UTF-8。</param>
    /// <param name="newLine">换行符；默认使用当前系统换行符。</param>
    public CsvGridExportProvider(char delimiter = ',', Encoding? encoding = null, string? newLine = null)
    {
        if (delimiter is '"' or '\r' or '\n')
        {
            throw new ArgumentOutOfRangeException(nameof(delimiter), "CSV 分隔符不能是引号或换行符。");
        }

        Delimiter = delimiter;
        Encoding = encoding ?? new UTF8Encoding(true);
        NewLine = newLine ?? Environment.NewLine;
    }

    /// <summary>获取字段分隔符。</summary>
    public char Delimiter { get; }

    /// <summary>获取输出文本编码。</summary>
    public Encoding Encoding { get; }

    /// <summary>获取输出换行符。</summary>
    public string NewLine { get; }

    /// <inheritdoc />
    public async Task ExportAsync(
        GridExportRequest request,
        Stream target,
        IProgress<GridExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(target);
        await using var writer = new StreamWriter(target, Encoding, 1024, leaveOpen: true) { NewLine = NewLine };
        if (request.Options.IncludeHeaders)
        {
            await WriteRowAsync(
                writer,
                request.Columns.Select(column => Escape(column.Header, isText: true, request.Options.FormulaInjectionPolicy)),
                cancellationToken);
        }

        for (var rowIndex = 0; rowIndex < request.Rows.Count; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var row = request.Rows[rowIndex];
            await WriteRowAsync(
                writer,
                row.Cells.Select(cell => Escape(
                    cell.DisplayText,
                    ShouldTreatAsText(cell, request.Options),
                    request.Options.FormulaInjectionPolicy)),
                cancellationToken);
            progress?.Report(new GridExportProgress(rowIndex + 1, request.Rows.Count));
        }

        await writer.FlushAsync(cancellationToken);
    }

    private async Task WriteRowAsync(
        TextWriter writer,
        IEnumerable<string> values,
        CancellationToken cancellationToken)
    {
        await writer.WriteLineAsync(string.Join(Delimiter, values).AsMemory(), cancellationToken);
    }

    private string Escape(string value, bool isText, GridFormulaInjectionPolicy formulaPolicy)
    {
        if (isText && formulaPolicy == GridFormulaInjectionPolicy.EscapeText && IsFormulaLike(value))
        {
            value = $"'{value}";
        }

        if (value.Contains(Delimiter) || value.Contains('"') || value.Contains('\r') || value.Contains('\n'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private static bool IsFormulaLike(string value)
    {
        var trimmed = value.TrimStart();
        return trimmed.Length > 0 && trimmed[0] is '=' or '+' or '-' or '@';
    }

    private static bool ShouldTreatAsText(GridExportCell cell, GridExportOptions options)
    {
        if (cell.RawValue is not (sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal))
        {
            return true;
        }

        return !decimal.TryParse(
            cell.DisplayText,
            System.Globalization.NumberStyles.Number,
            options.Culture,
            out _);
    }
}

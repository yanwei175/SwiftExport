using System.Globalization;
using System.IO;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using SwiftExportClient.Services.Files.Internal;

namespace SwiftExportClient.Services.Files.Csv;

/// <summary>
/// 使用 CsvHelper 在后台解析独立的 CSV 快照；重复、空表头和宽度差异都按索引保留并报告。
/// </summary>
public sealed class CsvFileReadService : ICsvReadService
{
    /// <inheritdoc />
    public Task<TableReadResult> ReadTableAsync(
        Stream source,
        CsvReadOptions? options = null,
        IProgress<FileOperationProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new CsvReadOptions();
        ArgumentNullException.ThrowIfNull(options.Encoding);
        ArgumentNullException.ThrowIfNull(options.Limits);
        options.Limits.Validate();
        if (string.IsNullOrEmpty(options.Delimiter) || options.Delimiter.IndexOfAny(['\r', '\n', '"', '\0']) >= 0)
        {
            throw new ArgumentException("CSV 分隔符不能为空或包含换行、引号、空字符。", nameof(options));
        }

        // 编码实例可以由调用方修改，因此在进入后台前取得私有副本并强制拒绝坏字节。
        var encoding = (Encoding)options.Encoding.Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        var snapshot = options with { Encoding = encoding };
        return Task.Run(() => ReadCoreAsync(source, snapshot, progress, cancellationToken), cancellationToken);
    }

    /// <summary>
    /// 先按字节上限取得私有输入，再解析；外部流和结果不与其它调用共享状态。
    /// </summary>
    private static async Task<TableReadResult> ReadCoreAsync(
        Stream source,
        CsvReadOptions options,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new FileOperationProgress("ReadingInput"));
            using var input = await FileInput.ReadBoundedAsync(source, options.Limits, cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(input, options.Encoding, detectEncodingFromByteOrderMarks: false);
            // 即使调用方选用“不写 BOM”的 Unicode 编码实例，读取仍接受该编码的 BOM；不猜测其它编码。
            if (options.Encoding.GetPreamble().Length == 0 &&
                options.Encoding.CodePage is 65001 or 1200 or 1201 or 12000 or 12001 && reader.Peek() == '\uFEFF')
            {
                reader.Read();
            }
            var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
            {
                Delimiter = options.Delimiter,
                HasHeaderRecord = options.HasHeaderRecord,
                IgnoreBlankLines = false,
                TrimOptions = TrimOptions.None,
                DetectDelimiter = false,
                ExceptionMessagesContainRawData = false,
                Encoding = options.Encoding
            };
            using var parser = new CsvParser(reader, configuration);
            return Parse(parser, options, progress, cancellationToken);
        }
        catch (DecoderFallbackException exception)
        {
            throw new FileOperationException("InvalidEncoding", "CSV 包含所选编码无法解码的字节。", exception);
        }
        catch (CsvHelperException exception)
        {
            throw new FileOperationException(
                "InvalidCsv", "CSV 引号或字段格式无效，文件未完成读取。", exception,
                [new FileIssue("InvalidCsv", "CSV 记录格式无效。", RowNumber: exception.Context?.Parser?.Row,
                    Severity: FileIssueSeverity.Error)]);
        }
        catch (IOException exception)
        {
            throw new FileOperationException("ReadFailed", "无法完成 CSV 输入读取。", exception);
        }
    }

    /// <summary>
    /// 每条逻辑记录保留实际字段数量；物理行范围放在来源地址，避免多行字段造成定位歧义。
    /// </summary>
    private static TableReadResult Parse(
        CsvParser parser,
        CsvReadOptions options,
        IProgress<FileOperationProgress>? progress,
        CancellationToken cancellationToken)
    {
        var headers = new List<FileCell>();
        var rows = new List<IReadOnlyList<FileCell>>();
        var issues = new List<FileIssue>();
        int? expectedColumns = null;
        long cellCount = 0;
        progress?.Report(new FileOperationProgress("ReadingCsv", 0));

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstPhysicalLine = parser.RawRow + 1;
            if (!parser.Read())
            {
                break;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var record = parser.Record ?? [];
            var isHeader = options.HasHeaderRecord && parser.Row == 1;
            cellCount += record.Length;
            if (record.Length > options.Limits.MaxColumns || cellCount > options.Limits.MaxCells ||
                (!isHeader && rows.Count >= options.Limits.MaxRows))
            {
                throw new FileOperationException("ReadLimitExceeded", "CSV 的行、列或单元格数量超过读取上限。",
                    issues: [new FileIssue("ReadLimitExceeded", "该记录超过读取上限。", RowNumber: parser.Row,
                        Severity: FileIssueSeverity.Error)]);
            }

            expectedColumns ??= record.Length;
            if (record.Length != expectedColumns)
            {
                issues.Add(new FileIssue("ColumnCountMismatch",
                    $"该记录有 {record.Length} 列，与首条记录的 {expectedColumns} 列不同；已保留全部实际字段。",
                    RowNumber: parser.Row));
            }

            var sourceAddress = $"CSV record {parser.Row}; lines {firstPhysicalLine}-{parser.RawRow}";
            var cells = record.Select((value, index) =>
                new FileCell(parser.Row, index + 1, FileCellKind.Text, value, value)
                {
                    SourceAddress = sourceAddress
                }).ToArray();
            if (isHeader)
            {
                headers.AddRange(cells);
                DiagnoseHeaders(headers, issues);
            }
            else
            {
                rows.Add(cells);
                if (rows.Count % 100 == 0)
                {
                    progress?.Report(new FileOperationProgress("ReadingCsv", rows.Count));
                }
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        var result = new TableReadResult(null, headers, rows, issues);
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new FileOperationProgress("Completed", rows.Count, rows.Count));
        return result;
    }

    /// <summary>
    /// 提醒调用方按列索引处理空名称和重复名称，不建立会覆盖字段的表头字典。
    /// </summary>
    private static void DiagnoseHeaders(IReadOnlyList<FileCell> headers, List<FileIssue> issues)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var header in headers)
        {
            if (header.DisplayText.Length == 0)
            {
                issues.Add(new FileIssue("EmptyHeader", "表头为空，读取结果仍保留该列。",
                    RowNumber: header.RowNumber, ColumnNumber: header.ColumnNumber));
            }
            else if (!names.Add(header.DisplayText))
            {
                issues.Add(new FileIssue("DuplicateHeader", "表头名称重复，读取结果按列索引保留。",
                    RowNumber: header.RowNumber, ColumnNumber: header.ColumnNumber));
            }
        }
    }
}

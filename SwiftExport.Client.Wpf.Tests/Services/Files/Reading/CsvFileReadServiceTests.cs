using System.Globalization;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Csv;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Reading;

/// <summary>验证 CSV 真实解析、来源定位、有界快照和调用方流的归属，不依赖 Excel 或 UI 线程。</summary>
[TestClass]
public sealed class CsvFileReadServiceTests
{
    private readonly CsvFileReadService _service = new();

    /// <summary>UTF-8 BOM、引号转义、多行字段和空字符串都必须按原文读取。</summary>
    [TestMethod]
    public async Task ReadTableAsync_PreservesQuotedTextAndPhysicalLineRange()
    {
        var content = "编号,说明,备注\r\n001,\"逗号,以及\"\"引号\"\"\",\r\n002,\"第一行\r\n第二行\",=SUM(A1:A2)\r\n003,  未裁剪  ,尾部\r\n";
        using var source = new MemoryStream(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(content)).ToArray());

        var result = await _service.ReadTableAsync(source);

        Assert.AreEqual("编号", result.Headers[0].RawValue);
        Assert.AreEqual(3, result.Rows.Count);
        Assert.AreEqual("001", result.Rows[0][0].RawValue);
        Assert.AreEqual("逗号,以及\"引号\"", result.Rows[0][1].RawValue);
        Assert.AreEqual(string.Empty, result.Rows[0][2].RawValue);
        Assert.AreEqual(FileCellKind.Text, result.Rows[0][2].Kind);
        Assert.AreEqual("第一行\r\n第二行", result.Rows[1][1].RawValue);
        Assert.AreEqual("=SUM(A1:A2)", result.Rows[1][2].RawValue);
        Assert.IsNull(result.Rows[1][2].Formula);
        Assert.AreEqual(3, result.Rows[1][0].RowNumber);
        Assert.AreEqual("CSV record 3; lines 3-4", result.Rows[1][0].SourceAddress);
        Assert.AreEqual("CSV record 4; lines 5-5", result.Rows[2][0].SourceAddress);
        Assert.AreEqual("  未裁剪  ", result.Rows[2][1].RawValue);
        Assert.AreEqual(0, result.Issues.Count);
    }

    /// <summary>重复及空表头不覆盖字段，较长和较短记录都保留其实际宽度。</summary>
    [TestMethod]
    public async Task ReadTableAsync_DiagnosesHeadersAndDifferentWidthsWithoutDataLoss()
    {
        using var source = Csv("名称,名称,\n甲,乙,丙,丁\n戊\n");

        var result = await _service.ReadTableAsync(source);

        Assert.AreEqual(3, result.Headers.Count);
        Assert.AreEqual(1, result.Headers[0].ColumnNumber);
        Assert.AreEqual(2, result.Headers[1].ColumnNumber);
        Assert.AreEqual(4, result.Rows[0].Count);
        Assert.AreEqual("丁", result.Rows[0][3].RawValue);
        Assert.AreEqual(1, result.Rows[1].Count);
        Assert.AreEqual(1, result.Issues.Count(issue => issue.Code == "DuplicateHeader"));
        Assert.AreEqual(1, result.Issues.Count(issue => issue.Code == "EmptyHeader"));
        Assert.AreEqual(2, result.Issues.Count(issue => issue.Code == "ColumnCountMismatch"));
        Assert.AreEqual(2, result.Issues.Single(issue => issue.Code == "DuplicateHeader").ColumnNumber);
    }

    /// <summary>无表头模式在不同文化下也不推断数字、日期、布尔值或分隔符。</summary>
    [TestMethod]
    [DataRow("zh-CN")]
    [DataRow("de-DE")]
    public async Task ReadTableAsync_NoHeaderAndExplicitDelimiterPreserveStrings(string cultureName)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            using var source = Csv("001;1,25;2026-10-12;true\n");
            var result = await _service.ReadTableAsync(source,
                new CsvReadOptions { HasHeaderRecord = false, Delimiter = ";" });

            Assert.AreEqual(0, result.Headers.Count);
            Assert.AreEqual(1, result.Rows.Count);
            CollectionAssert.AreEqual(new[] { "001", "1,25", "2026-10-12", "true" },
                result.Rows[0].Select(cell => cell.RawValue).ToArray());
            Assert.IsTrue(result.Rows[0].All(cell => cell.Kind == FileCellKind.Text));
            Assert.AreEqual(1, result.Rows[0][0].RowNumber);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    /// <summary>明确选定的非 UTF-8 编码能够读取，默认 UTF-8 不自动猜测或替换坏字节。</summary>
    [TestMethod]
    public async Task ReadTableAsync_ExplicitEncodingReadsLatin1AndDefaultRejectsIt()
    {
        var bytes = Encoding.Latin1.GetBytes("Nom\ncafé\n");
        using var explicitSource = new MemoryStream(bytes);
        var result = await _service.ReadTableAsync(explicitSource, new CsvReadOptions { Encoding = Encoding.Latin1 });
        Assert.AreEqual("café", result.Rows[0][0].RawValue);

        using var defaultSource = new MemoryStream(bytes);
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadTableAsync(defaultSource));
        Assert.AreEqual("InvalidEncoding", exception.Code);
        Assert.IsTrue(defaultSource.CanRead);
    }

    /// <summary>显式 UTF-16 接受自身 BOM，默认编码不得被 UTF-16 BOM 自动改写。</summary>
    [TestMethod]
    public async Task ReadTableAsync_ExplicitUnicodeAcceptsBomWithoutDetectingOtherEncodings()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("名称\r\n数据\r\n")).ToArray();
        using var explicitSource = new MemoryStream(bytes);
        var result = await _service.ReadTableAsync(explicitSource,
            new CsvReadOptions { Encoding = new UnicodeEncoding(false, false, true) });
        Assert.AreEqual("名称", result.Headers[0].RawValue);
        Assert.AreEqual("数据", result.Rows[0][0].RawValue);

        using var defaultSource = new MemoryStream(bytes);
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadTableAsync(defaultSource));
        Assert.AreEqual("InvalidEncoding", exception.Code);
    }

    /// <summary>空输入是空快照，空逻辑记录仍作为一个空文本字段保留。</summary>
    [TestMethod]
    public async Task ReadTableAsync_EmptyFileAndBlankRecordsRemainDistinguishable()
    {
        using var empty = Csv(string.Empty);
        var emptyResult = await _service.ReadTableAsync(empty);
        Assert.AreEqual(0, emptyResult.Headers.Count);
        Assert.AreEqual(0, emptyResult.Rows.Count);

        using var blank = Csv("名称\n\n下一行\n");
        var blankResult = await _service.ReadTableAsync(blank);
        Assert.AreEqual(2, blankResult.Rows.Count);
        Assert.AreEqual(string.Empty, blankResult.Rows[0][0].RawValue);
        Assert.AreEqual(FileCellKind.Text, blankResult.Rows[0][0].Kind);
        Assert.AreEqual("CSV record 3; lines 3-3", blankResult.Rows[1][0].SourceAddress);
    }

    /// <summary>只消费流当前位置之后的内容，成功后不关闭调用方的输入流。</summary>
    [TestMethod]
    public async Task ReadTableAsync_ReadsCurrentPositionAndLeavesSourceOpen()
    {
        using var source = Csv("ignored\n名称\n数据\n");
        source.Position = Encoding.UTF8.GetByteCount("ignored\n");

        var result = await _service.ReadTableAsync(source);

        Assert.AreEqual("名称", result.Headers[0].RawValue);
        Assert.AreEqual("数据", result.Rows[0][0].RawValue);
        Assert.IsTrue(source.CanRead);
        Assert.AreEqual(source.Length, source.Position);
    }

    /// <summary>不闭合引号整体失败，不泄漏记录文本，也不关闭外部输入。</summary>
    [TestMethod]
    public async Task ReadTableAsync_InvalidQuotingFailsWithSafeDiagnostics()
    {
        using var source = Csv("名称\n\"秘密值\n");

        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadTableAsync(source));

        Assert.AreEqual("InvalidCsv", exception.Code);
        Assert.IsFalse(exception.Message.Contains("秘密值", StringComparison.Ordinal));
        Assert.IsFalse(exception.Issues.Any(issue => issue.Message.Contains("秘密值", StringComparison.Ordinal)));
        Assert.IsTrue(exception.Issues.All(issue => issue.Severity == FileIssueSeverity.Error));
        Assert.AreEqual(2, exception.Issues[0].RowNumber);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>行、列、单元格限制均返回整体失败，不把超限输入当作成功的截断结果。</summary>
    [TestMethod]
    [DataRow("名称\n甲\n乙\n", 1, 256, 250000)]
    [DataRow("甲,乙,丙\n", 10000, 2, 250000)]
    [DataRow("甲,乙\n丙,丁\n", 10000, 256, 3)]
    public async Task ReadTableAsync_EnforcesResultLimits(string content, int rows, int columns, int cells)
    {
        using var source = Csv(content);
        var options = new CsvReadOptions
        {
            Limits = FileReadLimits.Default with { MaxRows = rows, MaxColumns = columns, MaxCells = cells }
        };

        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadTableAsync(source, options));

        Assert.AreEqual("ReadLimitExceeded", exception.Code);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>输入字节容量受共享边界保护，失败时仍由调用方释放流。</summary>
    [TestMethod]
    public async Task ReadTableAsync_EnforcesInputByteLimit()
    {
        using var source = Csv("名称\n数据\n");
        var options = new CsvReadOptions { Limits = FileReadLimits.Default with { MaxInputBytes = 5 } };

        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadTableAsync(source, options));

        Assert.AreEqual("InputTooLarge", exception.Code);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>预先取消不消费输入，也不转换为文件错误。</summary>
    [TestMethod]
    public async Task ReadTableAsync_PreCanceledDoesNotConsumeOrCloseInput()
    {
        using var source = Csv("名称\n数据\n");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.ReadTableAsync(source,
            cancellationToken: cancellation.Token));

        Assert.AreEqual(0L, source.Position);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>批量解析中合作取消停止后续记录，不发送成功阶段。</summary>
    [TestMethod]
    public async Task ReadTableAsync_CancelDuringParsingDoesNotReportCompleted()
    {
        using var source = Csv("名称\n" + string.Concat(Enumerable.Range(1, 2000).Select(index => $"{index}\n")));
        using var cancellation = new CancellationTokenSource();
        var stages = new List<string>();
        var progress = new CallbackProgress(value =>
        {
            stages.Add(value.Stage);
            if (value.Stage == "ReadingCsv" && value.ProcessedRows == 100)
            {
                cancellation.Cancel();
            }
        });

        await Assert.ThrowsAsync<OperationCanceledException>(() => _service.ReadTableAsync(source,
            progress: progress, cancellationToken: cancellation.Token));

        Assert.IsFalse(stages.Contains("Completed"));
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>同一单例服务的并行调用拥有独立解析状态，代表性 2,000 行不越界。</summary>
    [TestMethod]
    public async Task ReadTableAsync_ConcurrentCallsAndRepresentativeDataRemainIndependent()
    {
        using var first = Csv("甲\n" + string.Concat(Enumerable.Range(1, 2000).Select(index => $"A{index}\n")));
        using var second = Csv("乙\nB1\nB2\n");

        var results = await Task.WhenAll(_service.ReadTableAsync(first), _service.ReadTableAsync(second));

        Assert.AreEqual("甲", results[0].Headers[0].RawValue);
        Assert.AreEqual(2000, results[0].Rows.Count);
        Assert.AreEqual("A2000", results[0].Rows[^1][0].RawValue);
        Assert.AreEqual("乙", results[1].Headers[0].RawValue);
        Assert.AreEqual(2, results[1].Rows.Count);
        Assert.AreEqual("B2", results[1].Rows[^1][0].RawValue);
    }

    /// <summary>编码及语法错误的稳定分类与普通输入 I/O 错误保持区别。</summary>
    [TestMethod]
    public async Task ReadTableAsync_InputIoFailureHasStableCode()
    {
        using var source = new FailingInputStream();

        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadTableAsync(source));

        Assert.AreEqual("ReadFailed", exception.Code);
        Assert.IsTrue(source.CanRead);
    }

    /// <summary>建立只用于当前测试的 UTF-8 输入。</summary>
    private static MemoryStream Csv(string content) => new(Encoding.UTF8.GetBytes(content));

    /// <summary>在解析线程立即观察进度，避免测试依赖异步同步上下文派发时序。</summary>
    private sealed class CallbackProgress(Action<FileOperationProgress> callback) : IProgress<FileOperationProgress>
    {
        /// <summary>同步交付进度以触发确定性的合作取消。</summary>
        public void Report(FileOperationProgress value) => callback(value);
    }

    /// <summary>模拟文件读取失败，不需要真实磁盘故障或外部资源。</summary>
    private sealed class FailingInputStream : MemoryStream
    {
        /// <summary>拒绝异步读取，模拟调用方输入的 I/O 失败。</summary>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromException<int>(new IOException("测试读取失败。"));
    }
}

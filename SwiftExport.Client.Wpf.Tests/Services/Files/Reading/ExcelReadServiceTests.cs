using System.Globalization;
using System.IO;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Excel;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Reading;

/// <summary>通过真实 XLSX 流验证公共读取布局、缓存、来源和读取保护；全部测试无需 Excel 或 STA。</summary>
[TestClass]
public sealed class ExcelReadServiceTests
{
    /// <summary>检查名称只返回快照，读取目标必须明确，输入流从当前位置读取且保持打开。</summary>
    [TestMethod]
    public async Task InspectAndRead_ShouldUseExplicitWorksheetAndLeaveCallerStreamOpen()
    {
        using var original = ExcelReadFixture.Create(workbook =>
        {
            workbook.AddWorksheet("First").Cell("A1").Value = "ignore";
            var data = workbook.AddWorksheet("Data");
            data.Cell("A1").Value = "Code";
            data.Cell("A2").Value = "actual";
        });
        var service = new ClosedXmlExcelReadService();
        var info = await service.InspectAsync(original);
        CollectionAssert.AreEqual(new[] { "First", "Data" }, info.WorksheetNames.ToArray());
        Assert.IsTrue(original.CanRead);
        Assert.AreEqual(original.Length, original.Position);

        // 前缀不属于本次 XLSX 输入，证明读取不会重置调用方流的位置。
        using var prefixed = new MemoryStream();
        prefixed.Write(new byte[] { 1, 2, 3 });
        prefixed.Write(original.ToArray());
        prefixed.Position = 3;
        var result = await service.ReadTableAsync(prefixed, "Data");
        Assert.AreEqual("Data", result.WorksheetName);
        Assert.AreEqual("actual", result.Rows[0][0].RawValue);
        Assert.IsTrue(prefixed.CanRead);

        original.Position = 0;
        var missing = await Assert.ThrowsAsync<FileOperationException>(() => service.ReadTableAsync(original, "Missing"));
        Assert.AreEqual("WorksheetNotFound", missing.Code);
        original.Position = 0;
        var unnamed = await Assert.ThrowsAsync<FileOperationException>(() => service.ReadTableAsync(original, ""));
        Assert.AreEqual("WorksheetRequired", unnamed.Code);
        Assert.AreEqual(0L, original.Position);
    }

    /// <summary>保留缓存原值、日期、前导零显示、错误值及空中间行，并忽略仅有样式的尾行尾列。</summary>
    [TestMethod]
    public async Task ReadTable_ShouldPreserveCachedValuesFormatsPositionsAndEmptyRowsWithoutCalculation()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Cell("B3").Value = "Code";
            sheet.Cell("C3").Value = "Date";
            sheet.Cell("D3").Value = "Flag";
            sheet.Cell("E3").Value = "Text";
            sheet.Cell("B5").Value = 123;
            sheet.Cell("B5").Style.NumberFormat.Format = "00000";
            sheet.Cell("C5").Value = new DateTime(2026, 10, 11);
            sheet.Cell("C5").Style.NumberFormat.Format = "yyyy-MM-dd";
            sheet.Cell("D5").Value = true;
            sheet.Cell("E5").Value = "=literal text";
            sheet.Cell("B7").FormulaA1 = "1+1";
            sheet.Cell("C7").FormulaA1 = "UNSUPPORTED_EXCEL_FUNCTION()";
            sheet.Cell("B8").FormulaA1 = "1/0";
            sheet.Cell("C8").FormulaA1 = "2+2";
            sheet.Cell("Z100").Style.NumberFormat.Format = "0.00";
        });
        ExcelReadFixture.SetFormulaCache(stream, 1, "B7", "777");
        ExcelReadFixture.SetFormulaCache(stream, 1, "C7", "42");
        ExcelReadFixture.SetFormulaCache(stream, 1, "B8", "#DIV/0!", "e");
        ExcelReadFixture.SetFormulaCache(stream, 1, "C8", null);
        var progress = new CaptureProgress();
        var result = await new ClosedXmlExcelReadService().ReadTableAsync(stream, "Input", new ExcelTableReadOptions
        {
            HeaderRow = 3,
            DataStartRow = 5,
            FirstColumn = 2,
            Culture = CultureInfo.InvariantCulture,
        }, progress);

        Assert.HasCount(4, result.Headers);
        Assert.HasCount(4, result.Rows);
        Assert.AreEqual(5, result.Rows[0][0].RowNumber);
        Assert.AreEqual(2, result.Rows[0][0].ColumnNumber);
        Assert.AreEqual("B5", result.Rows[0][0].Address);
        Assert.AreEqual(123d, result.Rows[0][0].RawValue);
        Assert.AreEqual("00123", result.Rows[0][0].DisplayText);
        Assert.AreEqual(new DateTime(2026, 10, 11), result.Rows[0][1].RawValue);
        Assert.AreEqual("2026-10-11", result.Rows[0][1].DisplayText);
        Assert.AreEqual(true, result.Rows[0][2].RawValue);
        Assert.AreEqual("=literal text", result.Rows[0][3].RawValue);
        Assert.IsNull(result.Rows[0][3].Formula);
        Assert.IsTrue(result.Rows[1].All(cell => cell.Kind == FileCellKind.Blank));
        Assert.AreEqual(6, result.Rows[1][0].RowNumber);
        Assert.AreEqual(777d, result.Rows[2][0].RawValue);
        Assert.AreEqual("1+1", result.Rows[2][0].Formula);
        Assert.AreEqual(42d, result.Rows[2][1].RawValue);
        Assert.AreEqual(FileCellKind.Error, result.Rows[3][0].Kind);
        Assert.AreEqual("#DIV/0!", result.Rows[3][0].DisplayText);
        Assert.IsNull(result.Rows[3][1].RawValue);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "FormulaCacheMissing" && issue.Address == "C8"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "ExcelCellError" && issue.Address == "B8"));
        Assert.AreEqual(new FileOperationProgress("ReadExcelTable", 4, 4), progress.Last);
    }

    /// <summary>显式列范围只按范围内内容确定末行，范围外备注不扩大明细。</summary>
    [TestMethod]
    public async Task ReadTable_ShouldRespectExplicitColumnRange()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Cell("B1").Value = "Code";
            sheet.Cell("B2").Value = "one";
            sheet.Cell("F100").Value = "outside";
        });
        var result = await new ClosedXmlExcelReadService().ReadTableAsync(stream, "Input", new ExcelTableReadOptions
        {
            FirstColumn = 2,
            LastColumn = 3,
        });
        Assert.HasCount(2, result.Headers);
        Assert.HasCount(1, result.Rows);
        Assert.AreEqual("B2", result.Rows[0][0].Address);
        Assert.AreEqual(FileCellKind.Blank, result.Rows[0][1].Kind);
    }

    /// <summary>固定单头合并字段取锚点，明细合并不填充；末行只依据缓存值且保留中间空行。</summary>
    [TestMethod]
    public async Task ReadLayout_ShouldResolveHeaderAnchorKeepMergedDetailBlankAndUseCachedEndColumn()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Range("A1:C1").Merge();
            sheet.Cell("A1").Value = "Customer";
            sheet.Cell("A3").Value = "first";
            sheet.Cell("A5").Value = "last";
            sheet.Range("B3:B4").Merge();
            sheet.Cell("B3").Value = 12;
            sheet.Cell("B5").Value = 30;
            sheet.Cell("A10").FormulaA1 = "1+1";
        });
        ExcelReadFixture.SetFormulaCache(stream, 1, "A10", null);
        var layout = new ExcelLayoutDefinition
        {
            Fields = [new("Customer", "$C$1"), new("Code", "A3", true), new("Qty", "B3", true)],
            EndMode = ExcelLayoutEndMode.LastNonEmptyColumn,
            LastRowColumn = "A",
        };
        var result = await new ClosedXmlExcelReadService().ReadLayoutAsync(stream, "Input", layout);
        Assert.AreEqual("Customer", result.Header["Customer"].RawValue);
        Assert.AreEqual("C1", result.Header["Customer"].Address);
        Assert.AreEqual("A1", result.Header["Customer"].SourceAddress);
        Assert.AreEqual("A1:C1", result.Header["Customer"].MergedRange);
        Assert.HasCount(3, result.Rows);
        Assert.AreEqual(4, result.Rows[1]["Code"].RowNumber);
        Assert.IsNull(result.Rows[1]["Qty"].RawValue);
        Assert.AreEqual("B3:B4", result.Rows[1]["Qty"].MergedRange);
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "MergedDetailCell" && issue.Address == "B4" && issue.FieldKey == "Qty"));
        Assert.IsTrue(result.Issues.Any(issue => issue.Code == "FormulaCacheMissing" && issue.Address == "A10"));
    }

    /// <summary>汇总边界排除汇总所在行；结果字典保留纯字段键和原位置，不反射业务模型。</summary>
    [TestMethod]
    public async Task ReadLayout_ShouldStopBeforeExplicitSummaryRow()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Cell("D4").Value = 1;
            sheet.Cell("D6").Value = 3;
            sheet.Cell("D7").Value = 4;
        });
        var result = await new ClosedXmlExcelReadService().ReadLayoutAsync(stream, "Input", new ExcelLayoutDefinition
        {
            Fields = [new("Line.Amount", "D4", true)],
            EndMode = ExcelLayoutEndMode.BeforeSummaryCell,
            SummaryCellAddress = "D7",
        });
        Assert.HasCount(3, result.Rows);
        Assert.AreEqual(4, result.Rows[0]["Line.Amount"].RowNumber);
        Assert.IsNull(result.Rows[1]["Line.Amount"].RawValue);
        Assert.AreEqual(3d, result.Rows[2]["Line.Amount"].RawValue);
    }

    /// <summary>布局结构错误不消耗输入，不允许不同明细起点、重复键、范围地址或倒置汇总边界。</summary>
    [TestMethod]
    public async Task ReadLayout_ShouldRejectInvalidStructureBeforeReadingInput()
    {
        var definitions = new[]
        {
            new ExcelLayoutDefinition { Fields = [new("x", "A2", true), new("y", "B3", true)], LastRowColumn = "A" },
            new ExcelLayoutDefinition { Fields = [new("x", "A1"), new("x", "B1")] },
            new ExcelLayoutDefinition { Fields = [new("x", "A1:B2")] },
            new ExcelLayoutDefinition { Fields = [new("x", "A4", true)], EndMode = ExcelLayoutEndMode.BeforeSummaryCell, SummaryCellAddress = "A4" },
        };
        foreach (var layout in definitions)
        {
            using var stream = new MemoryStream([1, 2, 3]);
            var exception = await Assert.ThrowsAsync<FileOperationException>(() =>
                new ClosedXmlExcelReadService().ReadLayoutAsync(stream, "Input", layout));
            Assert.AreEqual("InvalidExcelLayout", exception.Code);
            Assert.AreEqual(0L, stream.Position);
        }
    }

    /// <summary>超过快照或输入边界时整体失败，取消保持控制流并保留调用方流。</summary>
    [TestMethod]
    public async Task Read_ShouldRejectLimitsCorruptionAndCancellationWithoutPartialResult()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Cell("A1").Value = "Code";
            sheet.Cell("A2").Value = "one";
            sheet.Cell("A3").Value = "two";
        });
        var service = new ClosedXmlExcelReadService();
        var rows = await Assert.ThrowsAsync<FileOperationException>(() => service.ReadTableAsync(stream, "Input", new ExcelTableReadOptions
        {
            Limits = new FileReadLimits { MaxRows = 1 },
        }));
        Assert.AreEqual("ReadSnapshotTooLarge", rows.Code);
        stream.Position = 0;
        var bytes = await Assert.ThrowsAsync<FileOperationException>(() => service.InspectAsync(stream, new FileReadLimits { MaxInputBytes = 1 }));
        Assert.AreEqual("InputTooLarge", bytes.Code);
        stream.Position = 0;
        var expanded = await Assert.ThrowsAsync<FileOperationException>(() => service.InspectAsync(stream, new FileReadLimits { MaxExpandedBytes = 1 }));
        Assert.AreEqual("ExpandedInputTooLarge", expanded.Code);
        stream.Position = 0;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => service.ReadTableAsync(stream, "Input", cancellationToken: cancellation.Token));
        Assert.IsTrue(stream.CanRead);
        using var corrupt = new MemoryStream([1, 2, 3]);
        var invalid = await Assert.ThrowsAsync<FileOperationException>(() => service.InspectAsync(corrupt));
        Assert.AreEqual("InvalidXlsx", invalid.Code);
        Assert.IsTrue(corrupt.CanRead);
    }

    /// <summary>列数和包含表头的单元格数分别受限，读取中的合作取消不返回部分快照。</summary>
    [TestMethod]
    public async Task ReadTable_ShouldEnforceColumnsAndCellLimitsAndCooperateWithCancellation()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Cell("A1").Value = "Code";
            sheet.Cell("B1").Value = "Qty";
            for (var row = 2; row <= 201; row++)
                sheet.Cell(row, 1).Value = row;
        });
        var service = new ClosedXmlExcelReadService();
        foreach (var limits in new[]
        {
            new FileReadLimits { MaxColumns = 1 },
            new FileReadLimits { MaxCells = 401 },
        })
        {
            stream.Position = 0;
            var exception = await Assert.ThrowsAsync<FileOperationException>(() =>
                service.ReadTableAsync(stream, "Input", new ExcelTableReadOptions { Limits = limits }));
            Assert.AreEqual("ReadSnapshotTooLarge", exception.Code);
        }
        stream.Position = 0;
        using var cancellation = new CancellationTokenSource();
        var progress = new CancelAfterProgress(cancellation);
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.ReadTableAsync(stream, "Input", progress: progress, cancellationToken: cancellation.Token));
        Assert.AreEqual(100, progress.Completed);
        Assert.IsTrue(stream.CanRead);
    }

    /// <summary>末行模式接受数值零，不把空白文本或只有样式的单元格算作最后一条明细。</summary>
    [TestMethod]
    public async Task ReadLayout_ShouldCountZeroAsContentAndExcludeWhitespaceAndStyleOnlyTail()
    {
        using var stream = ExcelReadFixture.Create(workbook =>
        {
            var sheet = workbook.AddWorksheet("Input");
            sheet.Cell("A2").Value = 0;
            sheet.Cell("A5").Value = "   ";
            sheet.Cell("A50").Style.NumberFormat.Format = "0.00";
        });
        var result = await new ClosedXmlExcelReadService().ReadLayoutAsync(stream, "Input", new ExcelLayoutDefinition
        {
            Fields = [new("Count", "A2", true)],
            LastRowColumn = "A",
        });
        Assert.HasCount(1, result.Rows);
        Assert.AreEqual(0d, result.Rows[0]["Count"].RawValue);
    }

    private sealed class CaptureProgress : IProgress<FileOperationProgress>
    {
        public FileOperationProgress? Last { get; private set; }
        public void Report(FileOperationProgress value) => Last = value;
    }

    private sealed class CancelAfterProgress(CancellationTokenSource cancellation) : IProgress<FileOperationProgress>
    {
        public int Completed { get; private set; }
        public void Report(FileOperationProgress value)
        {
            Completed = value.ProcessedRows ?? 0;
            if (Completed >= 100)
                cancellation.Cancel();
        }
    }
}

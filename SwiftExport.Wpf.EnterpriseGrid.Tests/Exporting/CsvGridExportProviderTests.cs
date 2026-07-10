using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;

namespace SwiftExport.Wpf.EnterpriseGrid.Tests.Exporting;

[TestClass]
public sealed class CsvGridExportProviderTests
{
    [TestMethod]
    public async Task ExportAsync_ShouldEscapeCsvNeutralizeTextFormulaKeepNumericAndReportProgress()
    {
        var options = new GridExportOptions { Culture = System.Globalization.CultureInfo.InvariantCulture };
        var request = new GridExportRequest(
            new[]
            {
                new GridExportColumn("text", "文本", null, 0, null),
                new GridExportColumn("number", "数值", null, 1, null)
            },
            new[]
            {
                new GridExportRow(new[]
                {
                    new GridExportCell("=2+2", "=2+2"),
                    new GridExportCell(-5.5m, "-5.5")
                }),
                new GridExportRow(new[]
                {
                    new GridExportCell("a,b\n\"c\"", "a,b\n\"c\""),
                    new GridExportCell(10m, "10")
                })
            },
            options);
        var progress = new CaptureProgress();
        await using var stream = new MemoryStream();

        await new CsvGridExportProvider(newLine: "\n").ExportAsync(request, stream, progress);

        Assert.IsTrue(stream.CanWrite);
        stream.Position = 0;
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
        var csv = await reader.ReadToEndAsync();
        Assert.AreEqual("文本,数值\n'=2+2,-5.5\n\"a,b\n\"\"c\"\"\",10\n", csv);
        Assert.AreEqual(new GridExportProgress(2, 2), progress.Last);
    }

    [TestMethod]
    public async Task ExportAsync_ShouldHonorCancellationAndLeaveCallerStreamOpen()
    {
        var request = new GridExportRequest(
            new[] { new GridExportColumn("c", "C", null, 0, null) },
            new[] { new GridExportRow(new[] { new GridExportCell("x", "x") }) },
            new GridExportOptions());
        await using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            new CsvGridExportProvider().ExportAsync(request, stream, cancellationToken: cancellation.Token));
        Assert.IsTrue(stream.CanWrite);
    }

    private sealed class CaptureProgress : IProgress<GridExportProgress>
    {
        public GridExportProgress? Last { get; private set; }

        public void Report(GridExportProgress value) => Last = value;
    }
}

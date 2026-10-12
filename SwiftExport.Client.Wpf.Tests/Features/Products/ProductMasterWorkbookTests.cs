using System.IO;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Services.Files.Reading;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.ImportExport;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Models;
using SwiftExportClient.Features.Products.ProductMasterImportExport.Services;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Excel;

namespace SwiftExport.Client.Wpf.Tests.Features.Products;

/// <summary>以真实 XLSX 验证十一字段平表、公式缓存、限额和原子发布，不依赖 Excel 安装。</summary>
[TestClass]
public sealed class ProductMasterWorkbookTests
{
    private readonly ProductMasterWorkbookService _service = new(new ClosedXmlExcelReadService());

    /// <summary>空白模板的首行就是识别字段，必填主型号为红色且不包含被退出的字段。</summary>
    [TestMethod]
    public async Task Template_HasElevenHeadersAndNoSampleData()
    {
        using var files = new Files();
        await _service.WriteTemplateAsync(new(files.Output));
        using var workbook = new XLWorkbook(files.Output);
        var sheet = workbook.Worksheet("父级型号");
        CollectionAssert.AreEqual(ProductMasterWorkbookService.Columns.Select(c => c.Header).ToArray(), sheet.Row(1).CellsUsed().Select(c => c.GetString()).ToArray());
        Assert.AreEqual(11, sheet.Row(1).CellsUsed().Count());
        Assert.AreEqual(XLColor.Red, sheet.Cell(1,1).Style.Font.FontColor);
        Assert.AreEqual("@", sheet.Cell(2,1).Style.NumberFormat.Format);
        Assert.AreEqual(1, sheet.SheetView.SplitRow);
        Assert.AreEqual(0, (await _service.ReadAsync(files.Output, "父级型号", default)).Rows.Count);
    }

    /// <summary>列顺序无关；格式化编码保留前导零，空白行被跳过且文件原行号稳定。</summary>
    [TestMethod]
    [DataRow("zh-CN")]
    [DataRow("de-DE")]
    public async Task Read_ReorderedHeadersAndFormattedCodesPreserveRows(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        try
        {
        using var files = new Files();
        using var content = ExcelReadFixture.Create(book =>
        {
            var sheet = book.AddWorksheet("Sheet1");
            sheet.Cell("A1").Value = "默认供应商编码"; sheet.Cell("B1").Value = "主型号"; sheet.Cell("C1").Value = "每箱净重(KG)"; sheet.Cell("D1").Value = "类型";
            sheet.Cell("A2").Value = "001"; sheet.Cell("B2").Value = 7; sheet.Cell("B2").Style.NumberFormat.Format = "0000"; sheet.Cell("C2").Value = 1.25; sheet.Cell("D2").Value = "忽略";
            sheet.Cell("B4").Value = "002"; sheet.Cell("C4").Value = "无效文本";
        });
        await File.WriteAllBytesAsync(files.Input, content.ToArray());
        var result = await _service.ReadAsync(files.Input, "Sheet1", default);
        Assert.AreEqual(2, result.Rows.Count);
        Assert.AreEqual("0007", result.Rows[0].MasterSku);
        Assert.AreEqual("001", result.Rows[0].DefaultSupplierCode);
        Assert.AreEqual("1.25", result.Rows[0].DefaultNetWeight);
        Assert.AreEqual("无效文本", result.Rows[1].DefaultNetWeight);
        Assert.AreEqual(4, result.Rows[1].SourceRowNumber);
        Assert.AreEqual(string.Empty, result.Rows[1].DefaultDescriptionEn);
        StringAssert.Contains(result.Warning, "已忽略");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    /// <summary>重复、缺失或合并识别表头必须拒绝，不能猜测映射。</summary>
    [TestMethod]
    [DataRow("duplicate")]
    [DataRow("missing")]
    [DataRow("merged")]
    public async Task InvalidHeaders_RejectWholeRead(string kind)
    {
        using var files = new Files();
        using var content = ExcelReadFixture.Create(book =>
        {
            var sheet = book.AddWorksheet("Sheet1");
            sheet.Cell("A1").Value = kind == "missing" ? "型号未知" : "主型号";
            sheet.Cell("B1").Value = kind == "duplicate" ? "主型号" : "中文描述";
            if (kind == "merged") sheet.Range("A1:B1").Merge();
            sheet.Cell("A2").Value = "N-1";
        });
        await File.WriteAllBytesAsync(files.Input, content.ToArray());
        await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadAsync(files.Input, "Sheet1", default));
    }

    /// <summary>公式只能读取已有缓存；缺失或错误缓存在对应字段上保留可修正错误。</summary>
    [TestMethod]
    public async Task FormulaCache_UsesStoredValueAndMarksMissingOrError()
    {
        using var files = new Files();
        using var content = ExcelReadFixture.Create(book =>
        {
            var sheet = book.AddWorksheet("Sheet1"); sheet.Cell("A1").Value = "主型号"; sheet.Cell("B1").Value = "每箱净重(KG)";
            for (var row = 2; row <= 4; row++) { sheet.Cell(row,1).Value = "N-" + row; sheet.Cell(row,2).FormulaA1 = "1+2"; }
        });
        ExcelReadFixture.SetFormulaCache(content, 1, "B2", "17.5");
        ExcelReadFixture.SetFormulaCache(content, 1, "B3", null);
        ExcelReadFixture.SetFormulaCache(content, 1, "B4", "#VALUE!", "e");
        await File.WriteAllBytesAsync(files.Input, content.ToArray());
        var rows = (await _service.ReadAsync(files.Input, "Sheet1", default)).Rows;
        Assert.AreEqual("17.5", rows[0].DefaultNetWeight);
        Assert.IsFalse(rows[0].HasFileErrors);
        Assert.IsTrue(rows[1].HasFileErrors); Assert.IsTrue(rows[2].HasFileErrors);
        rows[1].DefaultNetWeight = "4";
        Assert.IsFalse(rows[1].HasFileErrors);
    }

    /// <summary>原始行上限和输出容量明确拒绝；取消或默认拒绝覆盖均不损坏原文件。</summary>
    [TestMethod]
    public async Task LimitsCancellationAndOverwrite_PreserveOutput()
    {
        using var files = new Files();
        using var content = ExcelReadFixture.Create(book =>
        {
            var sheet = book.AddWorksheet("Sheet1"); sheet.Cell("A1").Value = "主型号";
            for (var row = 2; row <= 1002; row++) sheet.Cell(row,1).Value = "N-" + row;
        });
        await File.WriteAllBytesAsync(files.Input, content.ToArray());
        await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.ReadAsync(files.Input, "Sheet1", default));
        await File.WriteAllTextAsync(files.Output, "original");
        await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.WriteTemplateAsync(new(files.Output)));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => _service.WriteTemplateAsync(new(files.Output, true), cancellation.Token));
        Assert.AreEqual("original", await File.ReadAllTextAsync(files.Output));
        await Assert.ThrowsExactlyAsync<FileOperationException>(() => _service.WriteAsync(new(files.Output, true), Enumerable.Range(0,10001).Select(_ => new ProductMasterExportRowDto()).ToArray()));
        Assert.AreEqual("original", await File.ReadAllTextAsync(files.Output));
    }

    /// <summary>输出纯文本不会转公式，数值保持类型，全部一万行可被底层服务读回。</summary>
    [TestMethod]
    public async Task Export_TenThousandRowsRoundTripWithoutFormulaInjection()
    {
        using var files = new Files();
        var rows = Enumerable.Range(0,10000).Select(n => new ProductMasterExportRowDto
        {
            Data = new() { MasterSku = n == 0 ? "=1+1" : n.ToString("D6"), DefaultDescriptionZh = "+描述", DefaultPcsPerPackage = 12, DefaultNetWeight = 1.2345m, DefaultCbm = 0.000123m },
            CategoryPath = "工具 / 夹具", DefaultSupplierCode = "001"
        }).ToArray();
        await _service.WriteAsync(new(files.Output), rows);
        await using var stream = File.OpenRead(files.Output);
        var table = await new ClosedXmlExcelReadService().ReadTableAsync(stream, "父级型号");
        Assert.AreEqual(10000, table.Rows.Count);
        Assert.AreEqual("=1+1", table.Rows[0][0].DisplayText);
        Assert.IsNull(table.Rows[0][0].Formula);
        Assert.AreEqual("009999", table.Rows[^1][0].DisplayText);
        Assert.AreEqual(FileCellKind.Number, table.Rows[0][5].Kind);
        Assert.AreEqual("001", table.Rows[0][10].DisplayText);
    }

    /// <summary>仅管理本测试创建的临时文件，并在结束时清理。</summary>
    private sealed class Files : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), "SwiftExport-parent-import-" + Guid.NewGuid().ToString("N"));
        internal Files() => Directory.CreateDirectory(_root);
        internal string Input => Path.Combine(_root, "input.xlsx");
        internal string Output => Path.Combine(_root, "output.xlsx");
        /// <inheritdoc />
        public void Dispose() => Directory.Delete(_root, true);
    }
}


using System.IO;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExportClient.Services.Files;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Templates;

/// <summary>验证预制模板的真实渲染、公式保留、数据边界和流生命周期。</summary>
[TestClass]
public sealed class ClosedXmlReportTemplateExportServiceTests
{
    /// <summary>验证头字段、重复明细、样式与相对及绝对公式引用。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldRenderHeaderAndRepeatDetailsWithFormulaReferences()
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData());

        target.Position = 0;
        using var workbook = new XLWorkbook(target);
        var worksheet = workbook.Worksheet("订单");
        Assert.AreEqual("测试订单", worksheet.Cell("B1").CachedValue.GetText());
        Assert.AreEqual("客户甲", worksheet.Cell("B2").CachedValue.GetText());
        Assert.AreEqual("CODE-1", worksheet.Cell("B5").CachedValue.GetText());
        Assert.AreEqual("CODE-2", worksheet.Cell("B6").CachedValue.GetText());
        Assert.AreEqual(2d, worksheet.Cell("C5").CachedValue.GetNumber());
        Assert.AreEqual("ROUND(C5*D5,2)", worksheet.Cell("E5").FormulaA1);
        Assert.AreEqual("ROUND(C6*D6,2)", worksheet.Cell("E6").FormulaA1);
        Assert.AreEqual("C5*$D$2", worksheet.Cell("G5").FormulaA1);
        Assert.AreEqual("C6*$D$2", worksheet.Cell("G6").FormulaA1);
        Assert.AreEqual("#,##0.00", worksheet.Cell("E6").Style.NumberFormat.Format);
        Assert.IsTrue(worksheet.Cell("B1").Style.Font.Bold);
    }

    /// <summary>验证受控打印范围跟随明细扩展，并包含移动后的固定页脚。</summary>
    [TestMethod]
    [DataRow(2)]
    [DataRow(7)]
    public async Task ExportAsync_ShouldExpandDeclaredPrintAreaAndMoveFooter(int rows)
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData(rows: rows));
        target.Position = 0;
        using var workbook = new XLWorkbook(target);
        var worksheet = workbook.Worksheet("订单");
        var footerRow = rows + 6;
        Assert.AreEqual("固定页脚", worksheet.Cell(footerRow, 2).CachedValue.GetText());
        Assert.AreEqual($"CODE-{rows}", worksheet.Cell(rows + 4, 2).CachedValue.GetText());
        var area = worksheet.PageSetup.PrintAreas.Single();
        Assert.AreEqual(1, area.RangeAddress.FirstAddress.RowNumber);
        Assert.AreEqual(1, area.RangeAddress.FirstAddress.ColumnNumber);
        Assert.AreEqual(7, area.RangeAddress.LastAddress.ColumnNumber);
        Assert.AreEqual(footerRow, area.RangeAddress.LastAddress.RowNumber);
        Assert.IsTrue(area.Contains(worksheet.Cell(footerRow, 2)));
        Assert.IsTrue(area.Contains(worksheet.Cell(rows + 4, 2)));
    }

    /// <summary>验证常见公式前缀业务值均以文本输出，且保留普通空白。</summary>
    [TestMethod]
    [DataRow("=SUM(1,2)")]
    [DataRow("+命名文本")]
    [DataRow("-命名文本")]
    [DataRow("@命名文本")]
    [DataRow("&=SUM(1,2)")]
    public async Task ExportAsync_ShouldKeepBusinessFormulaPrefixesAsText(string title)
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData(title));
        target.Position = 0;
        using var workbook = new XLWorkbook(target);
        var worksheet = workbook.Worksheet("订单");
        Assert.IsFalse(worksheet.Cell("B1").HasFormula);
        Assert.AreEqual(title, worksheet.Cell("B1").CachedValue.GetText());
        Assert.IsFalse(worksheet.Cell("F5").HasFormula);
        Assert.AreEqual("=SUM(1,2)", worksheet.Cell("F5").CachedValue.GetText());
        Assert.AreEqual("  +保留文本  ", worksheet.Cell("B3").CachedValue.GetText());
    }

    /// <summary>验证零条及单条列表可以生成完整 XLSX，而不是残留 item 表达式。</summary>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    public async Task ExportAsync_ShouldHandleEmptyAndSingleDetail(int rows)
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData(rows: rows));
        var texts = TemplateTestFixture.TextValues(target);
        Assert.AreEqual(rows, texts.Count(t => t.StartsWith("CODE-", StringComparison.Ordinal)));
        Assert.IsFalse(texts.Any(t => t.Contains("{{item.", StringComparison.Ordinal)));
    }

    /// <summary>验证嵌套命名区域、分组行和 SUBTOTAL 公式确实由 Report 输出。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldRenderNestedGroupsAndSubtotalFormulas()
    {
        using var template = TemplateTestFixture.Open("嵌套分组模板.xlsx");
        using var target = new MemoryStream();
        var data = new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Title"] = "嵌套报表",
            ["Groups"] = new[]
            {
                new { Name = "外层甲", Items = new[]
                {
                    new { GroupName = "内层甲", Code = "A1", Quantity = 2, Price = 3m },
                    new { GroupName = "内层甲", Code = "A2", Quantity = 4, Price = 5m },
                    new { GroupName = "内层乙", Code = "A3", Quantity = 6, Price = 7m }
                } },
                new { Name = "外层乙", Items = new[]
                {
                    new { GroupName = "内层丙", Code = "B1", Quantity = 1, Price = 8m }
                } }
            }
        });
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, data);

        var xml = TemplateTestFixture.Xml(target, "xl/worksheets/sheet1.xml");
        var ns = TemplateTestFixture.Spreadsheet;
        var texts = TemplateTestFixture.TextValues(target);
        CollectionAssert.IsSubsetOf(new[] { "外层甲", "外层乙", "A1", "A2", "A3", "B1" }, texts);
        Assert.IsTrue(xml.Descendants(ns + "f").Count(f => f.Value.StartsWith("Subtotal(9,", StringComparison.OrdinalIgnoreCase)) >= 3);
        Assert.IsTrue(xml.Descendants(ns + "row").Any(r => (int?)r.Attribute("outlineLevel") > 0));
    }

    /// <summary>验证未实现函数不依赖客户端计算，公式与打开时全量计算标记完整保存。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldPreserveUnsupportedFormulasAndPrintSettings()
    {
        using var template = TemplateTestFixture.Open("公式打印模板.xlsx");
        using var target = new MemoryStream();
        var data = new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Title"] = "公式报表", ["Items"] = new[] { new { Code = "X1", Quantity = 2 }, new { Code = "X2", Quantity = 4 } }
        });
        await new ClosedXmlReportTemplateExportService().ExportAsync(template, target, data);

        var ns = TemplateTestFixture.Spreadsheet;
        var sheet = TemplateTestFixture.Xml(target, "xl/worksheets/sheet1.xml");
        var formulas = sheet.Descendants(ns + "c").Where(c => c.Element(ns + "f") is not null)
            .ToDictionary(c => (string)c.Attribute("r")!, c => c.Element(ns + "f")!.Value);
        Assert.AreEqual("UNSUPPORTED_FUNCTION(1)", formulas["F2"]);
        Assert.AreEqual("UNSUPPORTED_FUNCTION(C4)", formulas["E4"]);
        Assert.AreEqual("UNSUPPORTED_FUNCTION(C5)", formulas["E5"]);
        Assert.AreEqual("ROUND(C5*$B$2,2)", formulas["D5"]);
        Assert.AreEqual("landscape", (string?)sheet.Root!.Element(ns + "pageSetup")!.Attribute("orientation"));
        Assert.AreEqual("9", (string?)sheet.Root!.Element(ns + "pageSetup")!.Attribute("paperSize"));
        Assert.IsTrue(sheet.Descendants(ns + "mergeCell").Any(c => (string?)c.Attribute("ref") == "B1:E1"));

        var workbook = TemplateTestFixture.Xml(target, "xl/workbook.xml");
        var calculation = workbook.Root!.Element(ns + "calcPr")!;
        Assert.AreEqual("auto", (string?)calculation.Attribute("calcMode"));
        Assert.IsTrue(IsTrue(calculation.Attribute("fullCalcOnLoad")));
        Assert.IsTrue(IsTrue(calculation.Attribute("forceFullCalc")));
        Assert.IsTrue(workbook.Descendants(ns + "definedName").Any(n => (string?)n.Attribute("name") == "_xlnm.Print_Titles"));
    }

    /// <summary>验证业务数据中的 Report 标签不能注入单元格或被回传到诊断内容。</summary>
    [TestMethod]
    [DataRow("<<delete>>")]
    [DataRow("&=1+1<<OnlyValues>>")]
    [DataRow("文字>>")]
    public async Task ExportAsync_ShouldRejectReportTagSyntaxInBusinessData(string note)
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            new ClosedXmlReportTemplateExportService().ExportAsync(template, target, TemplateTestFixture.DetailData(note: note)));
        Assert.AreEqual("InvalidTemplateData", exception.Code);
        Assert.AreEqual("Note", exception.Issues.Single().FieldKey);
        Assert.AreEqual(FileIssueSeverity.Error, exception.Issues.Single().Severity);
        Assert.IsFalse(exception.Message.Contains(note, StringComparison.Ordinal));
        Assert.AreEqual(0, target.Length);
    }

    /// <summary>验证嵌套列表中的非法文本可以定位到属性路径。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldLocateReservedSyntaxInNestedField()
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        var data = new ExcelTemplateData(new Dictionary<string, object?>
        {
            ["Model"] = new { Items = new[] { new { Text = "<<bad>>" } } }
        });
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            new ClosedXmlReportTemplateExportService().ExportAsync(template, target, data));
        Assert.AreEqual("Model.Items[0].Text", exception.Issues.Single().FieldKey);
    }

    /// <summary>验证模板表达式失败时返回单元格位置，且不写入输出。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldAbortWhenReportHasParsingErrors()
    {
        using var fixture = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var invalidTemplate = new MemoryStream();
        using (var workbook = new XLWorkbook(fixture))
        {
            workbook.Worksheet("订单").Cell("B1").Value = "{{MissingField}}";
            workbook.SaveAs(invalidTemplate);
        }
        invalidTemplate.Position = 0;
        using var target = new MemoryStream();
        var exception = await Assert.ThrowsExactlyAsync<FileOperationException>(() =>
            new ClosedXmlReportTemplateExportService().ExportAsync(invalidTemplate, target, TemplateTestFixture.DetailData()));
        Assert.AreEqual("TemplateGenerationFailed", exception.Code);
        Assert.IsTrue(exception.Issues.Any(i => i.WorksheetName == "订单" && i.Address == "B1"));
        Assert.AreEqual(0, target.Length);
    }

    /// <summary>验证取消在生成阶段边界被观察，且调用方流继续可用。</summary>
    [TestMethod]
    [DataRow("GeneratingTemplate")]
    [DataRow("TemplateGenerated")]
    public async Task ExportAsync_ShouldCancelBeforeWritingTarget(string stage)
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        var progress = new TemplateTestFixture.StageProgress(p => { if (p.Stage == stage) cancellation.Cancel(); });
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(template, target, TemplateTestFixture.DetailData(), progress, cancellation.Token));
        Assert.AreEqual(0, target.Length);
        Assert.IsTrue(template.CanRead);
        Assert.IsTrue(target.CanWrite);
    }

    /// <summary>验证提前取消不消耗输入，并始终由调用方释放流。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldNotReadWhenAlreadyCanceled()
    {
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => new ClosedXmlReportTemplateExportService()
            .ExportAsync(template, target, TemplateTestFixture.DetailData(), cancellationToken: cancellation.Token));
        Assert.AreEqual(0, template.Position);
        Assert.IsTrue(template.CanRead);
        Assert.IsTrue(target.CanWrite);
    }

    /// <summary>验证同一无状态服务可并行生成不同数据而不串工作簿或变量。</summary>
    [TestMethod]
    public async Task ExportAsync_ShouldIsolateConcurrentOperationsAndLeaveStreamsOpen()
    {
        var service = new ClosedXmlReportTemplateExportService();
        await Task.WhenAll(Enumerable.Range(1, 6).Select(async index =>
        {
            using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
            using var target = new MemoryStream();
            await service.ExportAsync(template, target, TemplateTestFixture.DetailData($"并行-{index}"));
            Assert.IsTrue(template.CanRead);
            Assert.IsTrue(target.CanWrite);
            target.Position = 0;
            using var workbook = new XLWorkbook(target);
            Assert.AreEqual($"并行-{index}", workbook.Worksheet("订单").Cell("B1").CachedValue.GetText());
        }));
    }

    /// <summary>读取 Open XML 布尔属性的两种合法写法。</summary>
    private static bool IsTrue(XAttribute? attribute) => attribute?.Value is "1" or "true";
}

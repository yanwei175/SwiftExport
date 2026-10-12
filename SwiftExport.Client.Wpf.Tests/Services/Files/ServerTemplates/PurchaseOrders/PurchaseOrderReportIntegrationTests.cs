using System.IO;
using System.Xml.Linq;
using ClosedXML.Excel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.Integration;
using SwiftExport.Client.Wpf.Tests.Services.Files.Templates;
using SwiftExportClient.Services.Files.ServerTemplates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.ServerTemplates.PurchaseOrders;

/// <summary>用真实服务器模板目录、下载和 ClosedXML.Report 生成采购单示例，验证结构及快照语义，不计算公式。</summary>
[TestClass]
public sealed class PurchaseOrderReportIntegrationTests
{
    private const string MoneyFormat = "\"￥\"#,##0.00_);[Red](\"￥\"#,##0.00)";

    /// <summary>空、单条、多条及全空价格金额均保持布局、快照与模板公式；图片区域保持空白。</summary>
    [TestMethod]
    [DataRow(0, false)]
    [DataRow(1, false)]
    [DataRow(3, false)]
    [DataRow(3, true)]
    [DataRow(20, false)]
    public async Task ServerTemplate_ShouldGeneratePurchaseOrderSnapshot(int count, bool allAmountsNull)
    {
        await using var host = await StartHostAsync();
        using var client = new PurchaseOrderDemoClient(host);
        var info = (await client.Service.ListAsync(PurchaseOrderDemoFixture.Contract)).Single();
        Assert.AreEqual(PurchaseOrderDemoFixture.TemplateId, info.TemplateId);
        Assert.AreEqual(PurchaseOrderDemoFixture.Contract.DocumentType, info.DocumentType);
        Assert.AreEqual(1, info.TemplateVersion);
        Assert.AreEqual(1, info.DataContractVersion);
        var target = Path.Combine(host.Root, $"采购单-{count}-{allAmountsNull}.xlsx");
        try
        {
            await client.Service.ExportToFileAsync(new ServerTemplateExportRequest(info.TemplateId, info.TemplateVersion,
                PurchaseOrderDemoFixture.Contract, PurchaseOrderDemoFixture.Data(count, allAmountsNull)), target);
        }
        catch (Exception exception)
        {
            // 仅输出虚构夹具的完整异常链，便于区分模板表达式错误与 XLSX 部件兼容错误。
            Console.WriteLine(exception);
            if (exception is SwiftExportClient.Services.Files.FileOperationException failure)
                foreach (var issue in failure.Issues) Console.WriteLine(issue);
            throw;
        }
        Assert.AreEqual(1, host.Downloads);
        VerifyOutput(target, count, allAmountsNull, "DEMO-BO-0001");
    }

    /// <summary>未填写自定义采购号时模板显示系统单号，不把未知单号转换为额外业务值。</summary>
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("  ")]
    public async Task ServerTemplate_ShouldUseOrderNumberWhenBoNumberIsBlank(string? boNo)
    {
        await using var host = await StartHostAsync();
        using var client = new PurchaseOrderDemoClient(host);
        var target = Path.Combine(host.Root, "采购单号回退.xlsx");
        await client.Service.ExportToFileAsync(new ServerTemplateExportRequest(PurchaseOrderDemoFixture.TemplateId, 1,
            PurchaseOrderDemoFixture.Contract, PurchaseOrderDemoFixture.Data(1, boNo: boNo)), target);
        VerifyOutput(target, 1, false, "PO-DEMO-0001");
    }

    /// <summary>保存代表性三行真实生成结果供人工检查；只复制已通过结构验收的文件，不清理交付示例。</summary>
    [TestMethod]
    public async Task ServerTemplate_ShouldSaveRepresentativeArtifactForReview()
    {
        await using var host = await StartHostAsync();
        using var client = new PurchaseOrderDemoClient(host);
        var generated = Path.Combine(host.Root, "采购单三行示例.xlsx");
        await client.Service.ExportToFileAsync(new ServerTemplateExportRequest(PurchaseOrderDemoFixture.TemplateId, 1,
            PurchaseOrderDemoFixture.Contract, PurchaseOrderDemoFixture.Data(3)), generated);
        VerifyOutput(generated, 3, false, "DEMO-BO-0001");
        var root = WorkspaceRoot();
        var directory = Path.Combine(root, ".artifacts", "purchase-order-demo");
        Directory.CreateDirectory(directory);
        File.Copy(generated, Path.Combine(directory, "采购单导出示例.xlsx"), overwrite: true);
    }

    /// <summary>仅发布测试程序集中的受控模板副本，清单与正文走现有真实 HTTP 宿主。</summary>
    private static async Task<ServerTemplateApiHost> StartHostAsync()
    {
        var host = await ServerTemplateApiHost.StartAsync();
        try
        {
            await host.PublishTemplateBytesAsync(await PurchaseOrderDemoFixture.TemplateBytesAsync(),
                PurchaseOrderDemoFixture.TemplateId, PurchaseOrderDemoFixture.Contract.DocumentType,
                PurchaseOrderDemoFixture.ContractKey, 1);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    /// <summary>读取缓存值及原始 Open XML，检查格式与公式结构；绝不读取公式 Value 或主动触发计算。</summary>
    private static void VerifyOutput(string path, int count, bool allAmountsNull, string displayedOrderNo)
    {
        var totalRow = 9 + count;
        var notesRow = totalRow + 1;
        var signatureRow = totalRow + 2;
        using var workbook = new XLWorkbook(path);
        Assert.AreEqual(1, workbook.Worksheets.Count);
        var sheet = workbook.Worksheet(1);
        Assert.AreEqual(PurchaseOrderDemoFixture.Title, Text(sheet, "A1"));
        Assert.AreEqual("DEMO-C001", Text(sheet, "B2"));
        Assert.AreEqual("DEMO-PO-001", Text(sheet, "B3"));
        Assert.AreEqual(displayedOrderNo, Text(sheet, "B4"));
        Assert.AreEqual("演示业务员", Text(sheet, "B5"));
        Assert.AreEqual("演示跟单员", Text(sheet, "D5"));
        Assert.AreEqual("虚构演示供应商", Text(sheet, "F2"));
        Assert.AreEqual("演示进水管", Text(sheet, "F3"));
        Assert.AreEqual("演示起泡器", Text(sheet, "F4"));
        Assert.AreEqual("演示托盘", Text(sheet, "F5"));
        Assert.AreEqual("演示内盒", Text(sheet, "H2"));
        Assert.AreEqual("演示标贴", Text(sheet, "H3"));
        Assert.AreEqual("演示说明书", Text(sheet, "H4"));
        Assert.AreEqual("演示外箱", Text(sheet, "H5"));
        Assert.AreEqual("演示商标", Text(sheet, "J2"));
        Assert.AreEqual("特殊要求为示例文字；不包含真实业务数据。", Text(sheet, "B6"));
        Assert.AreEqual(new DateTime(2026, 10, 12), sheet.Cell("D2").CachedValue.GetDateTime());
        Assert.AreEqual(new DateTime(2026, 11, 15), sheet.Cell("D3").CachedValue.GetDateTime());
        Assert.AreEqual(new DateTime(2026, 11, 12), sheet.Cell("D4").CachedValue.GetDateTime());
        Assert.AreEqual("yyyy/m/d", sheet.Cell("D2").Style.NumberFormat.Format);
        Assert.AreEqual("微软雅黑", sheet.Cell("A1").Style.Font.FontName);
        Assert.AreEqual(18d, sheet.Cell("A1").Style.Font.FontSize);
        Assert.IsTrue(sheet.Cell("A1").Style.Font.Bold);
        Assert.IsTrue(sheet.Cell("A8").Style.Font.Bold);
        Assert.AreEqual(XLColor.FromHtml("#F2F2F2"), sheet.Cell("A8").Style.Fill.BackgroundColor);
        // 列宽按 1/256 字符量化，并含标准字体像素边距，允许百分之一字符的转换误差。
        Assert.AreEqual(19.08d, sheet.Column(1).Width, 0.01d);
        Assert.AreEqual(30.15d, sheet.Column(7).Width, 0.01d);
        Assert.AreEqual(45d, sheet.Row(1).Height);
        Assert.AreEqual(40.2d, sheet.Row(8).Height, 0.001d);
        var merges = sheet.MergedRanges.Select(range => range.RangeAddress.ToStringRelative(false)).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "A1:K1", "I2:I5", "J2:K5", "B6:K6", $"A{totalRow}:H{totalRow}", $"A{notesRow}:K{notesRow}" }, merges);
        Assert.AreEqual("合计", Text(sheet, $"A{totalRow}"));
        Assert.AreEqual(30d, sheet.Row(totalRow).Height);
        Assert.AreEqual(PurchaseOrderDemoFixture.NotesText, Text(sheet, $"A{notesRow}"));
        Assert.AreEqual(135d, sheet.Row(notesRow).Height);
        Assert.IsTrue(sheet.Cell($"A{notesRow}").Style.Alignment.WrapText);
        Assert.AreEqual("制单：", Text(sheet, $"B{signatureRow}"));
        Assert.AreEqual("审核：", Text(sheet, $"F{signatureRow}"));
        Assert.AreEqual("批准：", Text(sheet, $"I{signatureRow}"));
        Assert.AreEqual(30d, sheet.Row(signatureRow).Height);
        Assert.AreEqual(0, sheet.Pictures.Count);
        var rows = PurchaseOrderDemoFixture.Lines(count, allAmountsNull);
        for (var index = 0; index < rows.Length; ++index)
        {
            var row = index + 9;
            var line = rows[index];
            Assert.IsTrue(sheet.Cell(row, 1).CachedValue.IsBlank);
            Assert.AreEqual(line.CustomerSku, Text(sheet, $"B{row}"));
            Assert.AreEqual(line.LinkSku, Text(sheet, $"C{row}"));
            Assert.AreEqual(line.SupplierSku, Text(sheet, $"D{row}"));
            Assert.AreEqual(line.DescriptionZh, Text(sheet, $"E{row}"));
            Assert.AreEqual(line.FinishZh, Text(sheet, $"F{row}"));
            Assert.AreEqual(line.FeaturesZh, Text(sheet, $"G{row}"));
            Assert.AreEqual((double)line.Quantity, sheet.Cell(row, 9).CachedValue.GetNumber());
            Assert.AreEqual(line.SubNote, Text(sheet, $"K{row}"));
            Assert.IsFalse(sheet.Cell(row, 8).HasFormula);
            Assert.IsFalse(sheet.Cell(row, 10).HasFormula);
            Assert.AreEqual(MoneyFormat, sheet.Cell(row, 8).Style.NumberFormat.Format);
            Assert.AreEqual(MoneyFormat, sheet.Cell(row, 10).Style.NumberFormat.Format);
            Assert.AreEqual(120d, sheet.Row(row).Height);
            Assert.IsTrue(sheet.Cell(row, 5).Style.Alignment.WrapText);
            Assert.AreEqual(XLBorderStyleValues.Thin, sheet.Cell(row, 10).Style.Border.BottomBorder);
            if (allAmountsNull)
            {
                Assert.IsTrue(IsEmpty(sheet.Cell(row, 8).CachedValue));
                Assert.IsTrue(IsEmpty(sheet.Cell(row, 10).CachedValue));
            }
            else
            {
                Assert.AreEqual((double)line.UnitPrice!.Value, sheet.Cell(row, 8).CachedValue.GetNumber());
                Assert.AreEqual((double)line.UnitAmount!.Value, sheet.Cell(row, 10).CachedValue.GetNumber());
            }
        }
        if (count > 0 && !allAmountsNull)
        {
            Assert.AreEqual(99.99d, sheet.Cell("J9").CachedValue.GetNumber());
            Assert.AreNotEqual(12.50d * 3d, sheet.Cell("J9").CachedValue.GetNumber());
        }
        using var input = new MemoryStream(File.ReadAllBytes(path));
        var ns = TemplateTestFixture.Spreadsheet;
        var worksheet = TemplateTestFixture.Xml(input, "xl/worksheets/sheet1.xml");
        var formulas = worksheet.Descendants(ns + "c").Where(cell => cell.Element(ns + "f") is not null)
            .ToDictionary(cell => (string)cell.Attribute("r")!, cell => cell.Element(ns + "f")!.Value);
        Assert.AreEqual(2, formulas.Count);
        if (count == 0)
        {
            Assert.AreEqual("0", formulas[$"I{totalRow}"]);
            Assert.AreEqual("0", formulas[$"J{totalRow}"]);
        }
        else
        {
            // 末行由合计行自身确定，不让 Report 的二次引用位移把合计包含在求和范围内。
            Assert.AreEqual("SUM(I9:INDEX(I:I,ROW()-1))", formulas[$"I{totalRow}"]);
            Assert.AreEqual("IF(COUNT(J9:INDEX(J:J,ROW()-1))=0,\"\",SUM(J9:INDEX(J:J,ROW()-1)))",
                formulas[$"J{totalRow}"]);
        }
        Assert.AreEqual("portrait", (string?)worksheet.Root!.Element(ns + "pageSetup")!.Attribute("orientation"));
        Assert.AreEqual("9", (string?)worksheet.Root.Element(ns + "pageSetup")!.Attribute("paperSize"));
        var footer = worksheet.Root.Element(ns + "headerFooter")!.Element(ns + "oddFooter")!.Value;
        StringAssert.Contains(footer, "第 &P 页，共 &N 页");
        var workbookXml = TemplateTestFixture.Xml(input, "xl/workbook.xml");
        var printArea = workbookXml.Descendants(ns + "definedName").Single(name => (string?)name.Attribute("name") == "_xlnm.Print_Area").Value;
        Assert.AreEqual($"A1:K{signatureRow}", printArea.Split('!')[^1].Replace("$", string.Empty));
        Assert.AreEqual(signatureRow, sheet.PageSetup.PrintAreas.Single().RangeAddress.LastAddress.RowNumber);
        var titles = workbookXml.Descendants(ns + "definedName").Single(name => (string?)name.Attribute("name") == "_xlnm.Print_Titles").Value;
        // Excel 打印重复行允许带或不带绝对引用符号，两种序列化均须指向第 8 行。
        Assert.AreEqual("8:8", titles.Split('!')[^1].Replace("$", string.Empty));
        Assert.AreEqual(8, sheet.PageSetup.FirstRowToRepeatAtTop);
        Assert.AreEqual(8, sheet.PageSetup.LastRowToRepeatAtTop);
        var calculation = workbookXml.Root!.Element(ns + "calcPr")!;
        Assert.AreEqual("auto", (string?)calculation.Attribute("calcMode"));
        Assert.IsTrue(calculation.Attribute("fullCalcOnLoad")?.Value is "1" or "true");
        Assert.IsTrue(calculation.Attribute("forceFullCalc")?.Value is "1" or "true");
        Assert.IsFalse(TemplateTestFixture.TextValues(input).Any(value => value.Contains("{{", StringComparison.Ordinal)));
    }

    /// <summary>读取普通文本格的缓存，不调用显示格式化或公式求值。</summary>
    private static string Text(IXLWorksheet sheet, string address) => sheet.Cell(address).CachedValue.GetText();

    /// <summary>空金额允许 Open XML 表示为空格或空文本，但不能变成零或公式。</summary>
    private static bool IsEmpty(XLCellValue value) => value.IsBlank || (value.IsText && value.GetText().Length == 0);

    /// <summary>只在当前工程根下发布明确的演示工件，隔离测试输出路径变化不影响交付位置。</summary>
    private static string WorkspaceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md"))
                && File.Exists(Path.Combine(directory.FullName, "SwiftExport.Client.Wpf", "SwiftExport.Client.Wpf.csproj")))
                return directory.FullName;
        throw new InvalidOperationException("无法定位当前工程根目录，未发布采购单演示工件。");
    }
}

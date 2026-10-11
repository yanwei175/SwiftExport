using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.Wpf.Tests.Services.Files.Templates;
using SwiftExportClient.Bootstrapper;
using SwiftExportClient.Services.Files.Excel;
using SwiftExportClient.Services.Files.Templates;

namespace SwiftExport.Client.Wpf.Tests.Services.Files.Registration;

/// <summary>以代表性批量数据验证注册后的模板生成和独立读取可组合使用，不访问 UI、API 或 Office。</summary>
[TestClass]
public sealed class FileServiceScaleTests
{
    /// <summary>真实展开两千行并重新读取，验证首尾值、行数及展开公式，不用公式计算掩盖文件问题。</summary>
    [TestMethod]
    public async Task RegisteredServices_ShouldExportAndReadTwoThousandDetailRows()
    {
        const int rowCount = 2000;
        using var provider = new ServiceCollection().AddClientFileServices().BuildServiceProvider();
        using var template = TemplateTestFixture.Open("头与明细模板.xlsx");
        using var target = new MemoryStream();
        await provider.GetRequiredService<IExcelTemplateExportService>().ExportAsync(
            template, target, TemplateTestFixture.DetailData(rows: rowCount));

        var xml = TemplateTestFixture.Xml(target, "xl/worksheets/sheet1.xml");
        var ns = TemplateTestFixture.Spreadsheet;
        var lastAmount = xml.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == "E2004");
        Assert.AreEqual("ROUND(C2004*D2004,2)", lastAmount.Element(ns + "f")!.Value);
        var lastScaled = xml.Descendants(ns + "c").Single(cell => (string?)cell.Attribute("r") == "G2004");
        Assert.AreEqual("C2004*$D$2", lastScaled.Element(ns + "f")!.Value);

        target.Position = 0;
        var snapshot = await provider.GetRequiredService<IExcelReadService>().ReadLayoutAsync(target, "订单",
            new ExcelLayoutDefinition
            {
                Fields = [new("Customer", "B2"), new("Code", "B5", true), new("Quantity", "C5", true)],
                EndMode = ExcelLayoutEndMode.BeforeSummaryCell,
                SummaryCellAddress = "B2005",
            });

        Assert.HasCount(rowCount, snapshot.Rows);
        Assert.AreEqual("客户甲", snapshot.Header["Customer"].RawValue);
        Assert.AreEqual("CODE-1", snapshot.Rows[0]["Code"].RawValue);
        Assert.AreEqual(2d, snapshot.Rows[0]["Quantity"].RawValue);
        Assert.AreEqual("CODE-2000", snapshot.Rows[^1]["Code"].RawValue);
        Assert.AreEqual(2001d, snapshot.Rows[^1]["Quantity"].RawValue);
        Assert.AreEqual("B2004", snapshot.Rows[^1]["Code"].Address);
        Assert.HasCount(0, snapshot.Issues);
        Assert.IsTrue(template.CanRead);
        Assert.IsTrue(target.CanWrite);
    }
}

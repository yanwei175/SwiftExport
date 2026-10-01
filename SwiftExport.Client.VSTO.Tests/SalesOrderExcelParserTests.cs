using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Excel.Reading;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Services;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证真实模板快照、映射驱动解析及页面替换边界，不加载 Office 或连接数据库。</summary>
    [TestClass]
    public sealed class SalesOrderExcelParserTests
    {
        private static SalesOrderExcelParser Parser() => new SalesOrderExcelParser(ExcelImportRegistry.CreateDefault());
        private static ExcelFieldMappingDto Mapping(string label, string target) => new ExcelFieldMappingDto
            { GroupCode = "SalesOrder", ExcelHeader = label, TargetFieldKey = target, IsEnabled = true };
        private static ExcelFieldMappingDto[] Mappings() => new[]
        {
            Mapping("Ship to", "Header.ShipToAddress"), Mapping("Phone", "Header.ShipToPhone"),
            Mapping("PO No", "Header.CustomerPoNo"), Mapping("Invoice No", "Header.InvoiceNo"),
            Mapping("Date", "Header.OrderDate"), Mapping("ETD", "Header.Etd"), Mapping("Shipped By", "Header.ShippedBy"),
            Mapping("Customer Code", "Line.CustomerSku"), Mapping("Aquavit Code", "Line.LinkSku"),
            Mapping("Description", "Line.DescriptionEn"), Mapping("Finish", "Line.ColorNameEn"), Mapping("Features", "Line.FeaturesEn"),
            Mapping("FOB Jiangmen\n(USD/PC)", "Line.UsdPrice"), Mapping("FOB Jiangmen\n(CNY/PC)", "Line.CnyPrice"),
            Mapping("Quanity\n(PCS)", "Line.Qty"), Mapping("PCS/CTN", "Line.PcsOfCarton"),
            Mapping("GW/CTN\n(Kgs)", "Line.GwOfCarton"), Mapping("M3/CTN", "Line.CbmOfCarton")
        };
        // 从用户工作簿提取保存的原值和坐标；不依赖本机 Downloads 路径或格式化显示文字。
        private static ExcelRangeSnapshot Actual(string part)
        {
            var root = XDocument.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TestData", "销售订单47选区.xml")).Root;
            var node = root.Element(part);
            var values = new object[(int)node.Attribute("rows"), (int)node.Attribute("columns")];
            foreach (var cell in node.Elements("Cell"))
                values[(int)cell.Attribute("row"), (int)cell.Attribute("column")] = (string)cell.Attribute("kind") == "number"
                    ? (object)double.Parse(cell.Value, CultureInfo.InvariantCulture) : cell.Value;
            return new ExcelRangeSnapshot(values, (string)root.Attribute("source"), "Sheet1", part == "Header" ? "A11:P15" : "A16:P29",
                (int)node.Attribute("firstRow"), 1, false, (int)node.Attribute("headerRows"));
        }
        private static ExcelRangeSnapshot Snapshot(object[,] values, bool date1904 = false)
            => new ExcelRangeSnapshot(values, "test.xlsx", "Sheet1", "A1:D10", 1, 1, date1904);

        /// <summary>真实单头识别订单日期、采购号和原始条款，收货两格拼接不猜收货人。</summary>
        [TestMethod]
        public void ActualHeader()
        {
            var result = Parser().ParseHeader(Actual("Header"), Mappings());
            Assert.AreEqual("Order 47", result.Header.CustomerPoNo); Assert.AreEqual("260820", result.Header.InvoiceNo);
            Assert.AreEqual(new DateTime(2026, 8, 19), result.Header.OrderDate);
            StringAssert.StartsWith(result.Header.ShipToAddress, "LLC «AQUATORIA»\n125167");
            Assert.AreEqual("", result.Header.ShipToName); Assert.AreEqual("", result.Header.ShipToPhone); Assert.IsNull(result.Header.Etd);
            Assert.AreEqual("1x40HQ", result.Header.ShippedBy); StringAssert.StartsWith(result.Header.OriginalTerms, "FOB Jiangmen,30%");
            Assert.AreEqual("", result.Header.Incoterm); Assert.AreEqual(0, result.Warnings.Count);
            Assert.IsTrue(result.Header.Source.Cells.Any(c => c.Row == 13 && c.Column == 1));
        }
        /// <summary>真实九行在合并表头后开始，客户型号空白处结束，保留 LinkedSku 及统计原值。</summary>
        [TestMethod]
        public void ActualLinesAndTotals()
        {
            var result = Parser().ParseLines(Actual("Lines"), Mappings());
            Assert.AreEqual(9, result.Lines.Count); Assert.AreEqual(604m, result.Lines.Sum(l => l.Qty.Value));
            Assert.AreEqual(24590m, Math.Round(result.Lines.Sum(l => l.CnyAmount.Value), 2));
            Assert.AreEqual("AQ4101BG", result.Lines[0].CustomerSku); Assert.AreEqual("AA260011BG", result.Lines[0].OriginalLinkSku);
            Assert.IsNull(result.Lines[0].ProductId); Assert.AreEqual("", result.Lines[0].Sku);
            Assert.AreEqual("AQ4105BGM", result.Lines[8].CustomerSku); StringAssert.Contains(result.Message, "第 27 行");
            Assert.AreEqual(18, result.Lines[0].Source.Cells[0].Row); Assert.AreNotEqual("", result.Lines[0].OriginalAmountText);
        }
        /// <summary>支持 COM 的非零下界及单格快照，外部数组修改不改变快照。</summary>
        [TestMethod]
        public void SnapshotCopiesOneBasedArray()
        {
            var values = (object[,])Array.CreateInstance(typeof(object), new[] { 2, 1 }, new[] { 1, 1 });
            values[1, 1] = "Customer Code"; values[2, 1] = "A";
            var copy = Snapshot(values); values[2, 1] = "changed";
            Assert.AreEqual("A", copy[1, 0]); Assert.AreEqual(2, copy.RowCount);
            Assert.AreEqual(1, Snapshot(new object[,] { { "x" } }).ColumnCount);
        }
        /// <summary>第一条空客户型号立即结束；不能跳过空行，也不识别 TOTAL 为专用结束标记。</summary>
        [TestMethod]
        public void CustomerModelControlsTermination()
        {
            var result = Parser().ParseLines(Snapshot(new object[,] { { "Customer Code", "Description" }, { "TOTAL", "item" }, { " ", "stop" }, { "AFTER", "omitted" } }), Mappings());
            Assert.AreEqual(1, result.Lines.Count); Assert.AreEqual("TOTAL", result.Lines[0].CustomerSku);
            StringAssert.Contains(result.Message, "第 3 行");
        }
        /// <summary>非法数字保留文本，非法日期留空并提示，UpdateDate 只进入原文。</summary>
        [TestMethod]
        public void InvalidInputsAndSplitLabels()
        {
            var header = Parser().ParseHeader(Snapshot(new object[,] { { "Date:", "not a date" }, { "Invoice No", "00047" }, { "UpdateDate: 08/20/2026", null } }), Mappings());
            Assert.IsNull(header.Header.OrderDate); Assert.AreEqual("00047", header.Header.InvoiceNo);
            Assert.AreEqual("08/20/2026", header.Header.OriginalUpdateDateText); Assert.IsTrue(header.Warnings.Count > 0);
            var lines = Parser().ParseLines(Snapshot(new object[,] { { "Customer Code", "Quanity (PCS)" }, { "A", "not numeric" } }), Mappings());
            Assert.AreEqual("not numeric", lines.Lines[0].QtyText); Assert.IsNull(lines.Lines[0].Qty);
        }
        /// <summary>读取层提供日期系统后解析序号，不误用默认 OLE 日期且拒绝虚构日期。</summary>
        [TestMethod]
        public void BothExcelDateSystems()
        {
            var first = Parser().ParseHeader(Snapshot(new object[,] { { "Date", 1d } }), Mappings()).Header;
            Assert.AreEqual(new DateTime(1900, 1, 1), first.OrderDate);
            var second = Parser().ParseHeader(Snapshot(new object[,] { { "Date", 0d } }, true), Mappings()).Header;
            Assert.AreEqual(new DateTime(1904, 1, 1), second.OrderDate);
            var bad = Parser().ParseHeader(Snapshot(new object[,] { { "Date", 60d } }), Mappings());
            Assert.IsNull(bad.Header.OrderDate); Assert.IsTrue(bad.Warnings.Count > 0);
        }
        /// <summary>配置重复目标及缺失结束列拒绝导入，不取任意一个匹配结果。</summary>
        [TestMethod]
        public void AmbiguousMappingsAreRejected()
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => Parser().ParseLines(Snapshot(new object[,] { { "Description" }, { "x" } }), Mappings()));
            var aliases = new[] { Mapping("A", "Line.CustomerSku"), Mapping("B", "Line.CustomerSku") };
            Assert.ThrowsExactly<InvalidOperationException>(() => Parser().ParseLines(Snapshot(new object[,] { { "A", "B" }, { "x", "y" } }), aliases));
            Assert.ThrowsExactly<InvalidOperationException>(() => Parser().ParseHeader(Snapshot(new object[,] { { "Date: x" } }), new[] { Mapping("Date", "Header.CustomerId") }));
        }
        /// <summary>错误值不能成为客户型号，其他字段错误留空并保留来源供人工处理。</summary>
        [TestMethod]
        public void ExcelErrors()
        {
            var result = Parser().ParseLines(Snapshot(new object[,] { { "Customer Code", "Quanity (PCS)" }, { "A", new ErrorWrapper(2007) } }), Mappings());
            Assert.AreEqual("", result.Lines[0].QtyText); Assert.IsTrue(result.Warnings.Count > 0);
            Assert.ThrowsExactly<InvalidOperationException>(() => Parser().ParseLines(Snapshot(new object[,] { { "Customer Code" }, { new ErrorWrapper(2042) } }), Mappings()));
        }
        /// <summary>确认只替换相应部分，取消和区域选择取消保留原对象及手工编辑。</summary>
        [TestMethod]
        public async Task ReparseAndCancellation()
        {
            using (var f = new ImportTestFixture())
            {
                f.MappingApi.Mappings.AddRange(Mappings()); await f.Start(); f.Model.AddStandardLineCommand.Execute(null);
                var originalLine = f.Model.Lines[0]; var customer = f.Model.Header.CustomerId;
                f.Select = detail => Task.FromResult(Actual(detail ? "Lines" : "Header"));
                f.Confirm = false; var originalHeader = f.Model.Header;
                await f.Model.ParseHeaderCommand.ExecuteAsync(null); Assert.AreSame(originalHeader, f.Model.Header);
                f.Confirm = true; await f.Model.ParseHeaderCommand.ExecuteAsync(null);
                Assert.AreEqual(customer, f.Model.Header.CustomerId); Assert.AreSame(originalLine, f.Model.Lines[0]);
                Assert.AreEqual("260820", f.Model.Header.InvoiceNo); var parsedHeader = f.Model.Header;
                await f.Model.ParseLinesCommand.ExecuteAsync(null); Assert.AreSame(parsedHeader, f.Model.Header); Assert.AreEqual(9, f.Model.Lines.Count);
                var first = f.Model.Lines[0]; f.Select = _ => Task.FromResult<ExcelRangeSnapshot>(null);
                await f.Model.ParseLinesCommand.ExecuteAsync(null); Assert.AreSame(first, f.Model.Lines[0]);
                Assert.IsTrue(f.Model.IsEditable); Assert.IsTrue(f.Model.HasEdits);
            }
        }
        /// <summary>映射加载失败可重试，错误选区不能清空旧明细。</summary>
        [TestMethod]
        public async Task MappingFailureAndInvalidRangePreserveEdits()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.AddStandardLineCommand.Execute(null); var line = f.Model.Lines[0];
                f.MappingApi.Load = () => Task.FromResult(CacheRefreshResult.Fail("映射加载失败"));
                await f.Model.ParseLinesCommand.ExecuteAsync(null); Assert.AreEqual(0, f.Selections); Assert.AreSame(line, f.Model.Lines[0]);
                f.MappingApi.Load = () => Task.FromResult(CacheRefreshResult.Ok()); f.MappingApi.Mappings.AddRange(Mappings());
                f.Select = _ => Task.FromResult(Snapshot(new object[,] { { "wrong" }, { "value" } }));
                await f.Model.ParseLinesCommand.ExecuteAsync(null); Assert.AreSame(line, f.Model.Lines[0]); Assert.IsTrue(f.Model.IsEditable);
            }
        }
        /// <summary>选区框延迟期间门禁生效；账号切换或页面释放后旧快照不得回填。</summary>
        [TestMethod]
        public async Task SessionAndLifetimeRejectLateSelection()
        {
            foreach (var dispose in new[] { false, true })
            using (var f = new ImportTestFixture())
            {
                f.MappingApi.Mappings.AddRange(Mappings()); await f.Start();
                var pending = new TaskCompletionSource<ExcelRangeSnapshot>(); f.Select = _ => pending.Task;
                var running = f.Model.ParseHeaderCommand.ExecuteAsync(null);
                Assert.IsFalse(f.Model.ParseLinesCommand.CanExecute(null)); Assert.IsFalse(f.Model.IsEditable);
                if (dispose) f.Model.Dispose(); else f.Login("second");
                pending.SetResult(Actual("Header")); await running;
                Assert.AreEqual("", f.Model.Header.InvoiceNo); Assert.AreEqual("", f.Model.ParseMessage);
            }
        }
    }
}

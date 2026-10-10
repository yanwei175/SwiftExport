using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Services.ExcelImport;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证模型目录与受控赋值共用反射结果，不访问 Office、服务器或数据库。</summary>
    [TestClass]
    public sealed class ExcelImportReflectionTests
    {
        /// <summary>保留活动销售逻辑键，废弃汇率、身份、系统 SKU 和检查状态不进入目录。</summary>
        [TestMethod]
        public void SalesDirectoryComesFromModelAttributes()
        {
            var registry = ExcelImportRegistry.CreateDefault(); var definition = registry.Find(" salesorder ");
            Assert.AreEqual("SalesOrder", definition.GroupCode); Assert.AreEqual(3, registry.Groups.Count);
            Assert.AreEqual("PurchaseOrder", registry.Find("PurchaseOrder").GroupCode);
            Assert.AreEqual("PackingList", registry.Find("PackingList").GroupCode); Assert.IsNull(registry.Find("Unknown"));
            var existingKeys = new[] { "Header.InvoiceNo", "Header.CustomerPoNo", "Header.ShippedBy", "Header.Currency",
                "Header.ShipToName", "Header.ShipToAddress", "Header.ShipToPhone", "Header.Incoterm", "Header.IncotermLocation", "Header.Remark",
                "Header.OrderDate", "Header.DeliveryDate", "Header.Etd", "Header.Eta", "Line.CustomerSku", "Line.DescriptionEn", "Line.ColorNameEn",
                "Line.FeaturesEn", "Line.Qty", "Line.CnyPrice", "Line.UsdPrice", "Line.PcsOfCarton", "Line.NwOfCarton", "Line.GwOfCarton",
                "Line.CbmOfCarton", "Line.ExpectedDeliveryDate", "Line.SubNote" };
            foreach (var key in existingKeys) Assert.IsTrue(definition.ContainsField(key), key);
            foreach (var key in new[] { "Header.ExchangeRate", "Header.CustomerId", "Header.CustomerCode", "Header.OrderNo", "Line.ProductId", "Line.CustomerProductId",
                "Line.Sku", "Line.Uid", "Line.MatchStatus", "Line.AcceptLinkSkuMismatch", "Line.SearchCommand", "Line.QtyText" })
                Assert.IsFalse(definition.ContainsField(key), key);
            Assert.AreEqual("QtyText", definition.Fields.Single(v => v.FieldKey == "Line.Qty").PropertyName);
            Assert.AreEqual("OriginalLinkSku", definition.Fields.Single(v => v.FieldKey == "Line.LinkSku").PropertyName);
        }

        /// <summary>文字模型保留非法业务输入，身份字段或其他模型不能通过反射赋值。</summary>
        [TestMethod]
        public void AssignOnlyDeclaredFieldsAndRetainInvalidNumberText()
        {
            var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder"); var line = SalesOrderImportLine.CreateForImport();
            string error;
            Assert.IsTrue(definition.TryAssign(line, "Line.CustomerSku", "AQ4101BG", CultureInfo.InvariantCulture, out error));
            Assert.IsTrue(definition.TryAssign(line, "Line.LinkSku", "AA260011BG", CultureInfo.InvariantCulture, out error));
            Assert.IsTrue(definition.TryAssign(line, "Line.Qty", "bad", CultureInfo.InvariantCulture, out error));
            Assert.AreEqual("bad", line.QtyText); Assert.IsNull(line.Qty); Assert.IsTrue(line.Error.Contains("数量"));
            Assert.AreEqual("AA260011BG", line.OriginalLinkSku); Assert.AreEqual("", line.LinkSku); Assert.AreEqual("", line.Sku);
            Assert.IsFalse(definition.TryAssign(line, "Line.ProductId", Guid.NewGuid(), CultureInfo.InvariantCulture, out error));
            Assert.IsNull(line.ProductId);
            Assert.IsFalse(definition.TryAssign(line, "Header.InvoiceNo", "invoice", CultureInfo.InvariantCulture, out error));
            Assert.IsFalse(definition.TryAssign(new object(), "Line.Qty", "64", CultureInfo.InvariantCulture, out error));
            Assert.IsFalse(definition.TryAssign(line, "Line.QtyText", "64", CultureInfo.InvariantCulture, out error));
        }

        /// <summary>多源列冲突或类型转换失败时，本次赋值不部分覆盖用户已编辑的模型。</summary>
        [TestMethod]
        public void DuplicateTargetsAndConversionFailureDoNotPartiallyWrite()
        {
            var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder"); var header = new SalesOrderImportHeader { InvoiceNo = "old" };
            string error;
            Assert.IsFalse(definition.TryAssignValues(header, new[] { Pair("Header.InvoiceNo", "first"), Pair("Header.InvoiceNo", "second") }, CultureInfo.InvariantCulture, out error));
            Assert.IsTrue(error.Contains("多个源列")); Assert.AreEqual("old", header.InvoiceNo);
            Assert.IsFalse(definition.TryAssignValues(header, new[] { Pair("Header.InvoiceNo", "new"), Pair("Header.OrderDate", "invalid-date") }, CultureInfo.InvariantCulture, out error));
            Assert.AreEqual("old", header.InvoiceNo); Assert.AreEqual(DateTime.Today, header.OrderDate);
            Assert.IsFalse(definition.TryAssign(header, "Header.InvoiceNo", new object(), CultureInfo.InvariantCulture, out error));
            Assert.AreEqual("old", header.InvoiceNo);
        }

        /// <summary>日期文化由调用方明确提供；Excel 数值日期须由读取层辨别1900／1904体系。</summary>
        [TestMethod]
        public void DateConversionIsExplicitAndDoesNotGuessExcelSerials()
        {
            var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder"); var header = SalesOrderImportHeader.CreateForImport(); string error;
            Assert.IsTrue(definition.TryAssign(header, "Header.OrderDate", "08/19/2026", CultureInfo.GetCultureInfo("en-US"), out error));
            Assert.AreEqual(new DateTime(2026, 8, 19), header.OrderDate);
            Assert.IsFalse(definition.TryAssign(header, "Header.OrderDate", 46253d, CultureInfo.GetCultureInfo("en-US"), out error));
            Assert.AreEqual(new DateTime(2026, 8, 19), header.OrderDate);
            Assert.IsTrue(definition.TryAssign(header, "Header.Etd", "", CultureInfo.InvariantCulture, out error)); Assert.IsNull(header.Etd);
            Assert.IsFalse(definition.TryAssign(header, "Header.InvoiceNo", "inv", null, out error));
        }

        /// <summary>单个新属性标记即可进入目录，重复注册和私有setter误标记在启动时被发现。</summary>
        [TestMethod]
        public void ModelRegistrationRejectsInvalidDeclarations()
        {
            var valid = new ExcelImportDefinition("Test", "测试", typeof(TestHeader), typeof(TestLine));
            Assert.IsTrue(valid.ContainsField("Header.NewField"));
            Assert.ThrowsExactly<InvalidOperationException>(() => new ExcelImportRegistry(valid, valid));
            Assert.ThrowsExactly<InvalidOperationException>(() => new ExcelImportDefinition("Test", "测试", typeof(PrivateSetterHeader), typeof(TestLine)));
            Assert.ThrowsExactly<InvalidOperationException>(() => new ExcelImportDefinition("Test", "测试", typeof(DuplicateHeader), typeof(TestLine)));
        }

        /// <summary>旧汇率值不转换或赋值；其他已声明字段仍按原有目录正常导入。</summary>
        [TestMethod]
        public void DeprecatedSalesExchangeRateIsIgnoredDuringAssignment()
        {
            var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder");
            var header = new SalesOrderImportHeader { ExchangeRateText = "旧内存文本" }; string error;
            foreach (var value in new object[] { null, "bad", 0m, -7m, new object() })
            {
                Assert.IsTrue(definition.TryAssignValues(header, new[] { Pair("Header.ExchangeRate", value), Pair("Header.InvoiceNo", "INV") }, CultureInfo.InvariantCulture, out error), error);
                Assert.AreEqual("旧内存文本", header.ExchangeRateText); Assert.IsNull(header.ExchangeRate);
                Assert.AreEqual("INV", header.InvoiceNo); Assert.AreEqual("", header[nameof(header.ExchangeRateText)]);
            }
            Assert.IsFalse(ExcelAddressRules.IsDeprecatedField("PurchaseOrder", "Header.ExchangeRate"));
        }

        private static KeyValuePair<string, object> Pair(string key, object value) => new KeyValuePair<string, object>(key, value);
        /// <summary>模拟后续模型中新加的可导入属性，无额外字段目录声明。</summary>
        public sealed class TestHeader { [ExcelImportField("Header.NewField", "新字段")] public string NewField { get; set; } }
        /// <summary>模拟尚未标记任何输入字段的明细模型。</summary>
        public sealed class TestLine { public string InternalState { get; set; } }
        /// <summary>模拟误标记身份属性，公共getter不能代表可赋值。</summary>
        public sealed class PrivateSetterHeader { [ExcelImportField("Header.Id", "身份")] public string Id { get; private set; } }
        /// <summary>模拟两个属性误用同一个稳定键。</summary>
        public sealed class DuplicateHeader
        {
            [ExcelImportField("Header.Key", "甲")] public string First { get; set; }
            [ExcelImportField("Header.Key", "乙")] public string Second { get; set; }
        }
    }
}

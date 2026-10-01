using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证模型目录与受控赋值共用反射结果，不访问 Office、服务器或数据库。</summary>
    [TestClass]
    public sealed class ExcelImportReflectionTests
    {
        /// <summary>保留已有销售逻辑键，并只暴露标记属性，身份、系统 SKU 和检查状态不进入目录。</summary>
        [TestMethod]
        public void SalesDirectoryComesFromModelAttributes()
        {
            var registry = ExcelImportRegistry.CreateDefault(); var definition = registry.Find(" salesorder ");
            Assert.AreEqual("SalesOrder", definition.GroupCode); Assert.AreEqual(1, registry.Groups.Count);
            Assert.IsNull(registry.Find("PurchaseOrder")); Assert.IsNull(registry.Find("PackingList"));
            var existingKeys = new[] { "Header.InvoiceNo", "Header.CustomerPoNo", "Header.ShippedBy", "Header.Currency", "Header.ExchangeRate",
                "Header.ShipToName", "Header.ShipToAddress", "Header.ShipToPhone", "Header.Incoterm", "Header.IncotermLocation", "Header.Remark",
                "Header.OrderDate", "Header.DeliveryDate", "Header.Etd", "Header.Eta", "Line.CustomerSku", "Line.DescriptionEn", "Line.ColorNameEn",
                "Line.FeaturesEn", "Line.Qty", "Line.CnyPrice", "Line.UsdPrice", "Line.PcsOfCarton", "Line.NwOfCarton", "Line.GwOfCarton",
                "Line.CbmOfCarton", "Line.ExpectedDeliveryDate", "Line.SubNote" };
            foreach (var key in existingKeys) Assert.IsTrue(definition.ContainsField(key), key);
            foreach (var key in new[] { "Header.CustomerId", "Header.CustomerCode", "Header.OrderNo", "Line.ProductId", "Line.CustomerProductId",
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

        /// <summary>使用缓存别名配置匹配多行／全角表头，禁止双源覆盖和配置越界，不修改缓存DTO。</summary>
        [TestMethod]
        public void CachedMappingsResolveHeadersAndRejectConflicts()
        {
            var definition = ExcelImportRegistry.CreateDefault().Find("SalesOrder"); var line = SalesOrderImportLine.CreateForImport(); string error;
            var sku = Mapping("Customer Code", "Line.CustomerSku");
            var quantity = Mapping("Quantity\n(PCS)", "Line.Qty");
            var disabled = Mapping("Uid", "Line.Uid"); disabled.IsEnabled = false;
            var mappings = new[] { sku, quantity, disabled };
            Assert.IsTrue(definition.TryAssignMappedValues(line, mappings,
                new[] { Pair("Ｃｕｓｔｏｍｅｒ Code", "AQ4101BG"), Pair(" Quantity (PCS) ", 64d), Pair("Photo", "ignored") }, CultureInfo.InvariantCulture, out error));
            Assert.AreEqual("AQ4101BG", line.CustomerSku); Assert.AreEqual(64m, line.Qty);
            Assert.AreEqual("Quantity\n(PCS)", quantity.ExcelHeader); Assert.AreEqual("Line.Qty", quantity.TargetFieldKey);
            var alias = Mapping("数量", "Line.Qty");
            Assert.IsFalse(definition.TryAssignMappedValues(line, new[] { quantity, alias },
                new[] { Pair("Quantity (PCS)", "32"), Pair("数量", "99") }, CultureInfo.InvariantCulture, out error));
            Assert.AreEqual(64m, line.Qty);
            Assert.IsFalse(definition.TryAssignMappedValues(line, new[] { quantity, Mapping("quantity (pcs)", "Line.Qty") },
                new[] { Pair("Quantity (PCS)", "32") }, CultureInfo.InvariantCulture, out error));
            disabled.IsEnabled = true;
            Assert.IsFalse(definition.TryAssignMappedValues(line, mappings, new[] { Pair("Uid", Guid.NewGuid()) }, CultureInfo.InvariantCulture, out error));
            Assert.IsNull(line.ProductId);
            disabled.IsEnabled = false; quantity.GroupCode = "PurchaseOrder";
            Assert.IsFalse(definition.TryAssignMappedValues(line, mappings, new[] { Pair("Quantity (PCS)", "32") }, CultureInfo.InvariantCulture, out error));
        }

        private static ExcelFieldMappingDto Mapping(string header, string field) => new ExcelFieldMappingDto
        { GroupCode = "SalesOrder", ExcelHeader = header, TargetFieldKey = field, IsEnabled = true };

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

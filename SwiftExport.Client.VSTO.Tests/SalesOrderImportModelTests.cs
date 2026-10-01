using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证解析承载模型、通知、身份接纳和商业快照保留，不加载 Office。</summary>
    [TestClass]
    public sealed class SalesOrderImportModelTests
    {
        /// <summary>手工默认与导入缺失值分开，不能隐式补出有效订单日期和数量。</summary>
        [TestMethod]
        public void ImportDefaultsDoNotInventRequiredValues()
        {
            Assert.AreEqual(DateTime.Today, new SalesOrderImportHeader().OrderDate);
            Assert.AreEqual("1", new SalesOrderImportLine(false, null).QtyText);
            var header = SalesOrderImportHeader.CreateForImport();
            Assert.IsNull(header.OrderDate); Assert.IsNull(header.ExchangeRate); Assert.AreEqual("", header.Currency);
            Assert.IsTrue(header.Error.Contains("订单日期"));
            Assert.IsNull(SalesOrderImportLine.CreateForImport().Qty);
        }

        /// <summary>来源快照脱离外部列表；修正编辑值不改原文，解析错误进入既有汇总。</summary>
        [TestMethod]
        public void SourceSnapshotAndErrorsArePreserved()
        {
            var line = SalesOrderImportLine.CreateForImport();
            var source = new List<SalesOrderImportSourceCell> { new SalesOrderImportSourceCell(9, 12, "Quantity", "Line.Qty", "错误值") };
            line.Source.SetCells(source); source.Clear();
            line.Source.SetIssues(new[] { "数量解析失败" }); line.QtyText = "64";
            Assert.AreEqual("错误值", line.Source.Cells[0].Text); Assert.IsTrue(line.Error.Contains("数量解析失败"));
            line.Source.SetIssues(new string[0]); Assert.IsFalse(line.Error.Contains("数量解析失败"));
        }

        /// <summary>匹配只填写产品身份；价格、客户型号、描述、包装和原表型号保留。</summary>
        [TestMethod]
        public void MatchPreservesCommercialFieldsAndCopiesFeedback()
        {
            var line = SalesOrderImportLine.CreateForImport();
            line.CustomerSku = "AQ4101BG"; line.OriginalLinkSku = "AA260011BG";
            line.DescriptionEn = "Excel description"; line.ColorNameEn = "Brushed Gold"; line.FeaturesEn = "Line1\nLine2";
            line.QtyText = "64"; line.CnyPriceText = "37"; line.PcsOfCartonText = "32";
            var result = Match(line);
            Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision));
            Assert.AreEqual("SKU-001", line.Sku); Assert.AreEqual("SYSTEM-LINK", line.LinkSku);
            Assert.AreEqual("AA260011BG", line.OriginalLinkSku); Assert.AreEqual("Excel description", line.DescriptionEn);
            Assert.AreEqual("Brushed Gold", line.ColorNameEn); Assert.AreEqual("Line1\nLine2", line.FeaturesEn);
            Assert.AreEqual(37m, line.CnyPrice); Assert.AreEqual(32m, line.PcsOfCarton); Assert.AreEqual("PCS", line.UnitSnapshot);
            result.Candidates[0].Sku = "mutated"; result.Issues[0].Message = "mutated";
            Assert.AreEqual("SKU-001", line.MatchCandidates[0].Sku); Assert.AreEqual("需确认", line.CheckIssues[0].Message);
        }

        /// <summary>编辑后或错误行身份的迟到响应不能填写 ProductId；有效编辑复位检查状态。</summary>
        [TestMethod]
        public void StaleChecksAndIdentityChangesAreRejected()
        {
            var line = SalesOrderImportLine.CreateForImport(); var result = Match(line); var revision = line.EditRevision;
            line.QtyText = "64"; Assert.IsFalse(line.TryApplyCheck(result, revision)); Assert.IsNull(line.ProductId);
            result.ClientLineId = Guid.NewGuid(); Assert.IsFalse(line.TryApplyCheck(result, line.EditRevision));
            result.ClientLineId = line.Uid; Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision));
            line.AcceptLinkSkuMismatch = true; line.CustomerSku = "new-model";
            Assert.IsFalse(line.AcceptLinkSkuMismatch); Assert.IsNull(line.ProductId); Assert.AreEqual("", line.Sku); Assert.AreEqual("NotChecked", line.MatchStatus);
        }

        /// <summary>明确转换非标保留输入与稳定 UID，标准回转重新匹配且拒绝旧结果。</summary>
        [TestMethod]
        public void NonStandardConversionPreservesInputAndClearsIdentity()
        {
            var line = SalesOrderImportLine.CreateForImport(); line.DescriptionEn = "custom"; line.QtyText = "64";
            var result = Match(line); Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision)); var uid = line.Uid;
            line.SetNonStandard(true); Assert.IsNull(line.ProductId); Assert.IsNull(line.CustomerProductId);
            Assert.AreEqual("custom", line.DescriptionEn); Assert.AreEqual("64", line.QtyText); Assert.AreEqual(uid, line.Uid);
            Assert.IsTrue(line.Sku.StartsWith("NS-")); Assert.IsFalse(line.TryApplyCheck(result, line.EditRevision));
            var sku = line.Sku; line.SetNonStandard(false); Assert.AreEqual("", line.Sku);
            line.SetNonStandard(true); Assert.AreEqual(sku, line.Sku);
        }

        /// <summary>包装值非法文本保留且可定位，合法值通知其数值属性；旧手工选择仍保留正确身份。</summary>
        [TestMethod]
        public void PackagingNotificationsAndManualProductSelection()
        {
            var line = SalesOrderImportLine.CreateForImport(); var names = new List<string>();
            line.PropertyChanged += (sender, e) => names.Add(e.PropertyName);
            line.GwOfCartonText = "bad"; Assert.IsNull(line.GwOfCarton); Assert.AreEqual("bad", line.GwOfCartonText);
            Assert.IsTrue(line[nameof(line.GwOfCartonText)].Contains("每箱毛重"));
            line.GwOfCartonText = "5"; Assert.AreEqual(5m, line.GwOfCarton); Assert.IsTrue(names.Contains(nameof(line.GwOfCarton)));
            var id = Guid.NewGuid(); line.ApplyProduct(new SalesOrderProductLookupDto { ProductId = id, Sku = "SKU", CustomerSku = "customer-model" });
            Assert.AreEqual(id, line.ProductId); Assert.AreEqual("SKU", line.Sku);
        }

        /// <summary>失败或多候选结果只展示反馈，不残留上一次有效产品身份。</summary>
        [TestMethod]
        [DataRow("NotMatched")]
        [DataRow("Ambiguous")]
        [DataRow("Unavailable")]
        public void FailedMatchClearsPriorIdentity(string status)
        {
            var line = SalesOrderImportLine.CreateForImport(); var result = Match(line);
            Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision));
            result.MatchStatus = status; result.Product = null;
            Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision));
            Assert.IsNull(line.ProductId); Assert.IsNull(line.CustomerProductId); Assert.AreEqual("", line.Sku);
            Assert.AreEqual(status, line.MatchStatus); Assert.AreEqual(1, line.MatchCandidates.Count);
        }

        /// <summary>断链或缺少单位的 Matched 反馈不能获得身份，确认提示必须重新检查。</summary>
        [TestMethod]
        public void InvalidMatchAndWarningConfirmation()
        {
            var line = SalesOrderImportLine.CreateForImport(); var result = Match(line);
            result.Product.Unit = ""; Assert.IsFalse(line.TryApplyCheck(result, line.EditRevision)); Assert.IsNull(line.ProductId);
            result.Product.Unit = "PCS"; Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision));
            Assert.IsTrue(line.Error.Contains("需确认")); var revision = line.EditRevision;
            line.AcceptLinkSkuMismatch = true;
            Assert.AreEqual("NotChecked", line.MatchStatus); Assert.IsFalse(line.TryApplyCheck(result, revision));
            result.Issues[0].Acknowledged = true;
            Assert.IsTrue(line.TryApplyCheck(result, line.EditRevision)); Assert.IsFalse(line.Error.Contains("需确认"));
        }

        // 构造检查反馈，产品与客户关联身份必须完整、有效且单位非空。
        private static SalesOrderImportLineCheckDto Match(SalesOrderImportLine line)
        {
            var product = new SalesOrderImportProductCandidateDto { ProductId = Guid.NewGuid(), CustomerProductId = Guid.NewGuid(),
                CustomerSku = line.CustomerSku, Sku = "SKU-001", LinkSku = "SYSTEM-LINK", Unit = "PCS", IsUsable = true };
            return new SalesOrderImportLineCheckDto { ClientLineId = line.Uid, MatchStatus = "Matched", Product = product,
                Candidates = new List<SalesOrderImportProductCandidateDto> { product }, Issues = new List<SalesOrderImportIssueDto> {
                    new SalesOrderImportIssueDto { Code = "LinkSkuMismatch", Message = "需确认", RequiresConfirmation = true } } };
        }
    }
}

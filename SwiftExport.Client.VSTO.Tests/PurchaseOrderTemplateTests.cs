using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Excel.Writing.OrderExport;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证动态模板边界，防止合计覆盖明细或打印截断；不以此代替 Excel 格式实机验收。</summary>
    [TestClass]
    public sealed class PurchaseOrderTemplateTests
    {
        /// <summary>空模板、截图六行和多页明细均为合计及签字留出位置。</summary>
        [TestMethod]
        [DataRow(0, 9, 10)]
        [DataRow(6, 15, 16)]
        [DataRow(142, 151, 152)]
        [DataRow(1048566, 1048575, 1048576)]
        public void LayoutReservesFooter(int count, int total, int signature)
        {
            var layout = new PurchaseOrderTemplateLayout(count);
            Assert.AreEqual(total, layout.TotalRow);
            Assert.AreEqual(signature, layout.SignatureRow);
            Assert.AreEqual("$A$1:$K$" + signature, layout.PrintArea);
            Assert.AreEqual(count, layout.TotalRow - PurchaseOrderTemplateLayout.FirstDetailRow);
        }

        /// <summary>无效数量在调用任何 COM 之前拒绝，避免行号溢出和格式错位。</summary>
        [TestMethod]
        [DataRow(-1)]
        [DataRow(1048567)]
        [DataRow(int.MaxValue)]
        public void InvalidCountIsRejected(int count)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PurchaseOrderTemplateLayout(count));
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Services.OrderExport;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证采购导出快照、文件发布及真实 Refit 编排，不操作业务工作簿或真实服务器。</summary>
    [TestClass]
    public sealed class PurchaseOrderExportTests
    {
        /// <summary>行号决定顺序，本司型号取 LinkSku；空价格保持未知，优惠不参与合计。</summary>
        [TestMethod]
        public void SnapshotMappingOrderingAndTotals()
        {
            var detail = Detail(Guid.NewGuid()); detail.CustomerCode = "001-AT"; detail.CustomerPoNo = "=1+1";
            detail.BoNo = "BO-001"; detail.LogoType = "AQUATEK"; detail.DiscountRate = 0.95m; detail.DiscountAmount = 30; detail.DiscountRemark = "记录";
            detail.Lines.Add(new PurchaseOrderLineDetailDto { LineNumber = 2, LinkSku = "AA2", CustomerSku = "AQ2", Quantity = 4, UnitSnapshot = "PCS" });
            detail.Lines.Add(new PurchaseOrderLineDetailDto { LineNumber = 1, Sku = "REAL", LinkSku = "AA1", CustomerSku = "001", SupplierSku = "factory",
                Quantity = 2, UnitPrice = 5, UnitAmount = 10, UnitSnapshot = "PCS", MainImage = "https://should-not-load.invalid/p.png" });
            var document = new PurchaseOrderExportDocument(detail);
            Assert.AreEqual("001-AT", document.HeaderValues["B2"]); Assert.AreEqual("=1+1", document.HeaderValues["B3"]);
            Assert.AreEqual("AQUATEK", document.HeaderValues["J2"]); Assert.AreEqual(detail.OrderDate, document.HeaderValues["D2"]);
            Assert.AreEqual("001", document.DetailValues[0, 0]); Assert.AreEqual("AA1", document.DetailValues[0, 1]);
            Assert.AreEqual(10d, document.DetailValues[0, 8]); Assert.IsNull(document.DetailValues[1, 6]); Assert.IsNull(document.DetailValues[1, 8]);
            Assert.AreEqual(10, document.DetailValues.GetLength(1)); Assert.AreEqual("=SUM(I9:I10)", document.QuantityFormula);
            Assert.IsTrue(document.AmountFormula.Contains("SUM(J9:J10)")); Assert.AreEqual(11, document.Layout.TotalRow); Assert.AreEqual(16, document.Layout.SignatureRow);
            Assert.IsTrue(document.Warnings.Single().Contains("不完整")); Assert.IsTrue(document.Notes.Any(note => note.Contains("未抵扣")));
            detail.Lines[1].CustomerSku = "changed"; Assert.AreEqual("001", document.DetailValues[0, 0]);
        }

        /// <summary>空订单不能生成循环引用；混合单位只作输入统计。</summary>
        [TestMethod]
        public void EmptyAndMixedUnitDocuments()
        {
            var detail = Detail(Guid.NewGuid()); var empty = new PurchaseOrderExportDocument(detail);
            Assert.AreEqual("=0", empty.QuantityFormula); Assert.AreEqual("=0", empty.AmountFormula);
            detail.Lines.Add(new PurchaseOrderLineDetailDto { Quantity = 2, UnitSnapshot = "PCS", UnitPrice = 0, UnitAmount = 0 });
            detail.Lines.Add(new PurchaseOrderLineDetailDto { Quantity = 1, UnitSnapshot = "SET", UnitPrice = 0, UnitAmount = 0 });
            var mixed = new PurchaseOrderExportDocument(detail); Assert.AreEqual("合计（数量统计）", mixed.QuantityLabel);
            Assert.IsTrue(mixed.Warnings.Single().Contains("单位"));
            detail.Currency = "USD"; Assert.Throws<InvalidOperationException>(() => new PurchaseOrderExportDocument(detail));
        }

        /// <summary>同名文件保留先前内容，非法名称被清理，取消时不发布半成品。</summary>
        [TestMethod]
        public void FilesNeverOverwriteAndCancellationStopsPublication()
        {
            var directory = NewDirectory();
            try
            {
                var first = Path.Combine(directory, "tmp1.xlsx"); File.WriteAllText(first, "first");
                var path = PurchaseOrderExportFiles.Publish(first, directory, "采购:单/BO?1", CancellationToken.None);
                Assert.AreEqual("采购_单_BO_1.xlsx", Path.GetFileName(path));
                var second = Path.Combine(directory, "tmp2.xlsx"); File.WriteAllText(second, "second");
                var next = PurchaseOrderExportFiles.Publish(second, directory, "采购:单/BO?1", CancellationToken.None);
                Assert.AreNotEqual(path, next); Assert.AreEqual("first", File.ReadAllText(path)); Assert.AreEqual("second", File.ReadAllText(next));
                File.WriteAllText(second, "canceled");
                Assert.Throws<OperationCanceledException>(() => PurchaseOrderExportFiles.Publish(second, directory, "canceled", new CancellationToken(true)));
                Assert.IsFalse(File.Exists(Path.Combine(directory, "canceled.xlsx")));
                Assert.Throws<ArgumentException>(() => PurchaseOrderExportFiles.NormalizeDirectory("relative"));
                Assert.Throws<ArgumentException>(() => PurchaseOrderExportFiles.NormalizeDirectory("C:relative"));
            }
            finally { Directory.Delete(directory, true); }
        }

        /// <summary>真实详情 GET 路由，一单失败继续其余单，重复身份只导出一次。</summary>
        [TestMethod]
        public async Task BatchUsesDetailApiAndContinuesAfterFailure()
        {
            var first = Guid.NewGuid(); var failed = Guid.NewGuid(); var third = Guid.NewGuid(); var directory = NewDirectory();
            using (var fixture = new PurchaseOrderListTests.Fixture())
            using (var handler = new DetailsHandler())
            using (var clients = new ApiClientRegistration(new Uri("https://test.invalid/"), fixture.Session, new DetailsHandler(), handler))
            {
                handler.Get = id => Task.FromResult(id == failed ? ServiceResult<PurchaseOrderDetailDto>.Fail("订单无法读取") : ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(id)));
                var writer = new Writer(); var service = new PurchaseOrderExportService(clients.Create<IProcurementOrderApi>("api/procurement/orders"), writer,
                    fixture.Session, fixture, new ApiCallRunner(new AddInLog()));
                try
                {
                    var result = await service.ExportAsync(new[] { first, failed, third, first }, directory, null, CancellationToken.None);
                    CollectionAssert.AreEqual(new[] { first, failed, third }, handler.Ids); CollectionAssert.AreEqual(new[] { first, third }, writer.Ids);
                    Assert.AreEqual(2, result.Files.Count); Assert.AreEqual(1, result.FailedCount); Assert.IsTrue(result.Messages.Single().Contains("订单无法读取"));
                    Assert.IsTrue(handler.Paths.All(path => path.StartsWith("/api/procurement/orders/")));
                }
                finally { Directory.Delete(directory, true); }
            }
        }

        /// <summary>详情接口忽略取消返回旧账号数据时，不能调用工作簿写入器。</summary>
        [TestMethod]
        public async Task OldAccountDetailNeverReachesWriter()
        {
            var id = Guid.NewGuid(); var directory = NewDirectory();
            using (var fixture = new PurchaseOrderListTests.Fixture())
            using (var handler = new DetailsHandler())
            using (var clients = new ApiClientRegistration(new Uri("https://test.invalid/"), fixture.Session, new DetailsHandler(), handler))
            {
                var response = new TaskCompletionSource<ServiceResult<PurchaseOrderDetailDto>>(); var entered = new TaskCompletionSource<bool>();
                handler.Get = guid => { entered.SetResult(true); return response.Task; };
                var writer = new Writer(); var service = new PurchaseOrderExportService(clients.Create<IProcurementOrderApi>("api/procurement/orders"), writer,
                    fixture.Session, fixture, new ApiCallRunner(new AddInLog()));
                try
                {
                    var pending = service.ExportAsync(new[] { id }, directory, null, CancellationToken.None);
                    await entered.Task; fixture.Login("second"); response.SetResult(ServiceResult<PurchaseOrderDetailDto>.Ok(Detail(id)));
                    await Assert.ThrowsAsync<OperationCanceledException>(async () => await pending); Assert.AreEqual(0, writer.Ids.Count);
                }
                finally { Directory.Delete(directory, true); }
            }
        }

        private static PurchaseOrderDetailDto Detail(Guid id) => new PurchaseOrderDetailDto { PurchaseOrderId = id, OrderNo = "PO", Currency = "CNY", OrderDate = new DateTime(2026, 10, 8) };
        // 每个测试只创建、清理自己持有的唯一临时目录。
        private static string NewDirectory()
        { var path = Path.Combine(Path.GetTempPath(), "SwiftExport-PurchaseExportTests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(path); return path; }

        /// <summary>只记录传入快照，不引用 Office，也不创建真实工作簿。</summary>
        private sealed class Writer : IPurchaseOrderWorkbookWriter
        {
            internal readonly List<Guid> Ids = new List<Guid>();
            public Task<string> WriteAsync(PurchaseOrderExportDocument document, string directory, long version, CancellationToken token)
            { token.ThrowIfCancellationRequested(); Ids.Add(document.OrderId); return Task.FromResult(Path.Combine(directory, document.OrderId + ".xlsx")); }
        }

        /// <summary>内存 HTTP 返回真实共享 DTO，仅接受详情 GET，拒绝图片或额外请求。</summary>
        private sealed class DetailsHandler : HttpMessageHandler
        {
            internal readonly List<Guid> Ids = new List<Guid>(); internal readonly List<string> Paths = new List<string>();
            internal Func<Guid, Task<ServiceResult<PurchaseOrderDetailDto>>> Get;
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                Assert.AreEqual(HttpMethod.Get, request.Method);
                var path = request.RequestUri.AbsolutePath; Paths.Add(path); var id = Guid.Parse(path.Split('/').Last()); Ids.Add(id);
                var result = await Get(id);
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(result), Encoding.UTF8, "application/json") };
            }
        }
    }
}

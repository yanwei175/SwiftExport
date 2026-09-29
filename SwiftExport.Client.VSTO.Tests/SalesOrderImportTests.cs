using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Contracts.DTOs.Customers;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证导入草稿在本地编辑、异步查询和会话边界上的行为。</summary>
    [TestClass]
    public sealed class SalesOrderImportTests
    {
        /// <summary>首次按需加载，失败可重试，成功后重复打开不重复加载。</summary>
        [TestMethod]
        public async Task LazyLoadFailureRetryAndIdempotence()
        {
            using (var f = new ImportTestFixture())
            {
                Assert.AreEqual(0, f.Cache.Loads);
                f.Cache.Load = () => Task.FromResult(CacheRefreshResult.Fail("网络失败"));
                await f.Model.InitializeAsync(); Assert.IsFalse(f.Model.IsEditable);
                Assert.AreEqual("网络失败", f.Model.BusyMessage);
                f.Cache.Load = () => Task.FromResult(CacheRefreshResult.Ok());
                await f.Start(); await f.Model.InitializeAsync();
                Assert.AreEqual(2, f.Cache.Loads); Assert.IsTrue(f.Model.IsEditable);
            }
        }
        /// <summary>客户默认值是独立副本；取消切换保留明细，确认切换清空。</summary>
        [TestMethod]
        public async Task CustomerDefaultsAndDiscardConfirmation()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); var original = f.Model.SelectedCustomer;
                Assert.AreEqual("甲", f.Model.Header.ShipToName); Assert.AreEqual("地址", f.Model.Header.ShipToAddress);
                f.Model.Header.ShipToName = "手填"; Assert.AreEqual("甲", original.CustomerName);
                f.Model.AddStandardLineCommand.Execute(null); var line = f.Model.Lines[0];
                var other = new CustomerDto { Uid = Guid.NewGuid(), CustomerName = "乙", Currency = "CNY" };
                f.Confirm = false; f.Model.SelectedCustomer = other; f.Model.CustomerSelectedCommand.Execute(null);
                Assert.AreSame(original, f.Model.SelectedCustomer); Assert.AreSame(line, f.Model.Lines[0]); Assert.AreEqual("手填", f.Model.Header.ShipToName);
                f.Confirm = true; f.Model.SelectedCustomer = other; f.Model.CustomerSelectedCommand.Execute(null);
                Assert.AreEqual(0, f.Model.Lines.Count); Assert.AreEqual("乙", f.Model.Header.ShipToName);
                Assert.AreEqual("CNY", f.Model.Header.Currency); Assert.IsTrue(f.Model.CurrencyOptions.Any(c => c.CurrencyCode == "CNY"));
                f.Model.CustomerClearedCommand.Execute(null); Assert.AreEqual(Guid.Empty, f.Model.Header.CustomerId);
            }
        }
        /// <summary>手工明细保留非法文本，金额不换汇且多选删除同步汇总。</summary>
        [TestMethod]
        public async Task EditingValidationTotalsAndDelete()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.Header.DeliveryDate = new DateTime(2026, 10, 1);
                f.Model.AddNonStandardLineCommand.Execute(null); var line = f.Model.Lines[0];
                Assert.IsTrue(line.Sku.StartsWith("NS-")); Assert.IsTrue(line.ExpectedDeliveryDate == new DateTime(2026, 10, 1));
                line.DescriptionEn = "定制件"; line.UnitSnapshot = "PCS"; line.QtyText = "3"; line.UsdPriceText = "2"; line.CnyPriceText = "9";
                Assert.AreEqual(6m, f.Model.TotalUsdAmount); Assert.AreEqual(27m, f.Model.TotalCnyAmount);
                f.Model.Header.ExchangeRateText = "7"; Assert.AreEqual(6m, f.Model.TotalUsdAmount);
                line.QtyText = "bad"; Assert.AreEqual("bad", line.QtyText); Assert.IsNull(line.Qty); Assert.IsTrue(f.Model.ValidationSummary.Contains("汇总不完整"));
                line.QtyText = "3"; line.UsdPriceText = "-1"; Assert.IsNull(line.UsdPrice);
                f.Model.AddNonStandardLineCommand.Execute(null); f.Model.SetSelectedLines(f.Model.Lines.ToArray());
                f.Model.DeleteSelectedLineCommand.Execute(null); Assert.AreEqual(0, f.Model.Lines.Count); Assert.AreEqual(0m, f.Model.TotalQty);
            }
        }
        /// <summary>更新基础资料保留正在编辑的失效值并给出提示，不篡改共享集合。</summary>
        [TestMethod]
        public async Task CacheRefreshPreservesEditedValues()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.Header.PaymentMethod = "TT"; f.Cache.DictionaryItems.Clear();
                Assert.AreEqual("TT", f.Model.Header.PaymentMethod); Assert.AreEqual(0, f.Cache.DictionaryValues.Count);
                Assert.IsTrue(f.Model.PaymentMethodOptions.Any(v => v.DataNameEn == "TT")); Assert.IsTrue(f.Model.ValidationSummary.Contains("TT"));
            }
        }
        /// <summary>快速输入的旧结果不能覆盖新候选，选择只回填所属行。</summary>
        [TestMethod]
        public async Task LatestSearchWinsAndCommitPreservesManualValues()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.AddStandardLineCommand.Execute(null); var line = f.Model.Lines[0];
                var old = Pending(); var next = Pending();
                f.Api.Search = (r, t) => { Assert.AreEqual(f.Model.Header.CustomerId, r.CustomerId); Assert.AreEqual(30, r.PageSize); return r.Keyword == "old" ? old.Task : next.Task; };
                var first = line.SearchCommand.ExecuteAsync("old"); var second = line.SearchCommand.ExecuteAsync("new");
                next.SetResult(Result("NEW")); await second; old.SetResult(Result("OLD")); await first;
                Assert.AreEqual("NEW", line.ProductCandidates.Single().Sku);
                line.QtyText = "8"; line.SubNote = "保留"; line.SelectedProduct = line.ProductCandidates[0]; line.CommitProductCommand.Execute(null);
                Assert.AreEqual("NEW", line.Sku); Assert.AreEqual("8", line.QtyText); Assert.AreEqual("保留", line.SubNote);
            }
        }
        /// <summary>删行、换号、关闭均使迟到查询失效，不能污染新草稿。</summary>
        [TestMethod]
        public async Task RemovedSessionAndDisposedSearchesAreIgnored()
        {
            foreach (var mode in new[] { "delete", "session", "dispose" })
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.AddStandardLineCommand.Execute(null); var line = f.Model.Lines[0];
                var pending = Pending(); f.Api.Search = (r, t) => pending.Task;
                var query = line.SearchCommand.ExecuteAsync("sku");
                if (mode == "delete") { f.Model.SetSelectedLines(new[] { line }); f.Model.DeleteSelectedLineCommand.Execute(null); }
                if (mode == "session") { f.Login("second"); Assert.IsFalse(f.Model.IsEditable); Assert.IsFalse(f.Model.HasEdits); Assert.AreEqual(0, f.Model.Lines.Count); }
                if (mode == "dispose") f.Model.Dispose();
                pending.SetResult(Result("LATE")); await query;
                Assert.AreEqual(0, line.ProductCandidates.Count); Assert.IsNull(line.ProductId); Assert.IsFalse(line.SearchCommand.CanExecute("x"));
            }
        }
        /// <summary>旧加载返回后不能让已退出或释放的页面重新可编辑。</summary>
        [TestMethod]
        public async Task LateInitializationAndCloseGuard()
        {
            using (var f = new ImportTestFixture())
            {
                var pending = new TaskCompletionSource<CacheRefreshResult>(); f.Cache.Load = () => pending.Task;
                var load = f.Model.InitializeAsync(); await f.Model.InitializeAsync(); Assert.AreEqual(1, f.Cache.Loads);
                f.Session.TryEnd(f.Session.Capture().Version); pending.SetResult(CacheRefreshResult.Ok()); await load;
                Assert.IsFalse(f.Model.IsEditable); Assert.AreEqual(Guid.Empty, f.Model.Header.CustomerId);
                f.Login("new"); f.Cache.Load = () => Task.FromResult(CacheRefreshResult.Ok()); await f.Start();
                f.Confirm = false; Assert.IsFalse(f.Model.CanClose()); f.Confirm = true; Assert.IsTrue(f.Model.CanClose());
                f.Model.Dispose(); Assert.IsFalse(f.Model.AddStandardLineCommand.CanExecute(null));
            }
        }
        /// <summary>行查询相互独立；服务端拒绝或网络错误只提示，不退出账号或清除草稿。</summary>
        [TestMethod]
        public async Task IndependentRowsAndSearchFailureKeepDraft()
        {
            using (var f = new ImportTestFixture())
            {
                await f.Start(); f.Model.AddStandardLineCommand.Execute(null); f.Model.AddStandardLineCommand.Execute(null);
                var first = f.Model.Lines[0]; var second = f.Model.Lines[1];
                var a = Pending(); var b = Pending(); f.Api.Search = (r, t) => r.Keyword == "a" ? a.Task : b.Task;
                var queryA = first.SearchCommand.ExecuteAsync("a"); var queryB = second.SearchCommand.ExecuteAsync("b");
                b.SetResult(Result("B")); await queryB; a.SetResult(Result("A")); await queryA;
                Assert.AreEqual("A", first.ProductCandidates.Single().Sku); Assert.AreEqual("B", second.ProductCandidates.Single().Sku);
                f.Api.Search = (r, t) => Task.FromResult(ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>.Fail("没有查询权限"));
                await first.SearchCommand.ExecuteAsync("denied"); Assert.AreEqual("没有查询权限", f.Model.BusyMessage);
                f.Api.Search = (r, t) => throw new System.Net.Http.HttpRequestException();
                await first.SearchCommand.ExecuteAsync("network"); Assert.IsTrue(f.Model.BusyMessage.Contains("无法连接"));
                Assert.IsNotNull(f.Session.Capture().User); Assert.AreEqual(2, f.Model.Lines.Count); Assert.IsFalse(first.IsSearchBusy);
            }
        }
        /// <summary>页面释放后即使缓存加载成功也不能接受结果或再次启动加载。</summary>
        [TestMethod]
        public async Task DisposeDuringInitialLoadPreventsUpdates()
        {
            using (var f = new ImportTestFixture())
            {
                var pending = new TaskCompletionSource<CacheRefreshResult>(); f.Cache.Load = () => pending.Task;
                var load = f.Model.InitializeAsync(); f.Model.Dispose();
                pending.SetResult(CacheRefreshResult.Ok()); await load; await f.Model.InitializeAsync();
                Assert.IsFalse(f.Model.IsEditable); Assert.AreEqual(1, f.Cache.Loads);
            }
        }
        private static TaskCompletionSource<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>> Pending() => new TaskCompletionSource<ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>>();
        private static ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>> Result(string sku) => ServiceResult<PagedResponseBase<SalesOrderProductLookupDto>>.Ok(
            new PagedResponseBase<SalesOrderProductLookupDto> { Items = new[] { new SalesOrderProductLookupDto { ProductId = Guid.NewGuid(), Sku = sku, UsdPrice = 2m } }.ToList() });
    }
}

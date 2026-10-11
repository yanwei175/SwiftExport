using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.ViewModels;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Contracts.Apis.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Client.VSTO.Services.OrderExport;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证侧栏真实模型的查询条件、分页与账号生命周期，不加载 Office 或真实服务器。</summary>
    [TestClass]
    public sealed class PurchaseOrderListTests
    {
        /// <summary>首次使用才搜索，重复显示不清空勾选，筛选使用同一管理页 DTO。</summary>
        [TestMethod]
        public async Task LazyLoadFiltersAndPaging()
        {
            using (var fixture = new Fixture())
            {
                var model = fixture.Model;
                Assert.AreEqual(0, fixture.Api.Requests.Count);
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), model.OutputPath);
                await model.InitializeAsync();
                Assert.AreEqual(30, fixture.Api.Requests[0].PageSize);
                Assert.AreEqual(0, fixture.Api.Requests[0].ConfirmationStatuses.Count);
                Assert.IsNull(fixture.Api.Requests[0].CustomerCode);
                model.Orders[0].IsChecked = true; model.SelectedOrder = model.Orders[0];
                await model.InitializeAsync();
                Assert.AreEqual(1, fixture.Api.Requests.Count); Assert.AreEqual(1, model.CheckedCount);
                await model.NextPageCommand.ExecuteAsync(null);
                Assert.AreEqual(2, fixture.Api.Requests[1].PageIndex);
                Assert.AreEqual(0, model.CheckedCount); Assert.IsNull(model.SelectedOrder);
                model.Keyword = " invoice "; model.CustomerCode = " AT "; model.Confirmation = 1;
                model.QuickView = PurchaseDueViewEnum.OverdueNotReady;
                await model.SearchCommand.ExecuteAsync(null);
                var request = fixture.Api.Requests[2];
                Assert.AreEqual(1, request.PageIndex); Assert.AreEqual("invoice", request.Keyword);
                Assert.AreEqual("AT", request.CustomerCode);
                CollectionAssert.AreEqual(new[] { ConfirmationStatusEnum.已确认 }, request.ConfirmationStatuses);
                Assert.AreEqual(PurchaseDueViewEnum.OverdueNotReady, request.DueView);
                Assert.IsNull(model.Orders[0].Order.TotalQty); Assert.AreEqual(4.25m, model.Orders[0].Order.ReadyQty);
                Assert.AreEqual("部分货好", model.Orders[0].ReadyText); Assert.AreEqual("部分收", model.Orders[0].ReceiptText);
                Assert.AreEqual("活动", model.Orders[0].DocumentText);
            }
        }

        /// <summary>失败保留已有列表与输入，用户可以主动刷新；无查看权限不发起查询。</summary>
        [TestMethod]
        public async Task FailureRetryAndPermissionGate()
        {
            using (var fixture = new Fixture())
            {
                await fixture.Model.InitializeAsync();
                fixture.Api.Search = (r, t) => Task.FromResult(ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Fail("没有权限"));
                fixture.Model.Keyword = "keep";
                await fixture.Model.SearchCommand.ExecuteAsync(null);
                Assert.AreEqual("没有权限", fixture.Model.ErrorMessage); Assert.AreEqual("keep", fixture.Model.Keyword);
                Assert.AreEqual(1, fixture.Model.Orders.Count); Assert.IsFalse(fixture.Model.IsBusy);
                fixture.Api.Search = (r, t) => Task.FromResult(Fixture.Success());
                await fixture.Model.RefreshCommand.ExecuteAsync(null); Assert.AreEqual("", fixture.Model.ErrorMessage);
                fixture.Login("no-access", false);
                Assert.AreEqual(0, fixture.Model.Orders.Count); Assert.IsFalse(fixture.Model.SearchCommand.CanExecute(null));
                var calls = fixture.Api.Requests.Count;
                await fixture.Model.InitializeAsync(); Assert.AreEqual(calls, fixture.Api.Requests.Count);
            }
        }

        /// <summary>取消传输后即便旧请求仍成功，也不得回填到新账号或已释放页面。</summary>
        [TestMethod]
        public async Task LateResultCannotCrossSessionOrDisposal()
        {
            using (var fixture = new Fixture())
            {
                var response = new TaskCompletionSource<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>();
                fixture.Api.Search = (r, t) => response.Task;
                var pending = fixture.Model.InitializeAsync();
                Assert.IsTrue(fixture.Model.IsBusy); Assert.IsFalse(fixture.Model.RefreshCommand.CanExecute(null));
                fixture.Login("second");
                Assert.IsTrue(fixture.Api.LastToken.IsCancellationRequested);
                fixture.Api.Search = (r, t) => Task.FromResult(Fixture.Success("new-account"));
                await fixture.Model.RefreshCommand.ExecuteAsync(null);
                response.SetResult(Fixture.Success("old-account")); await pending;
                Assert.AreEqual("new-account", fixture.Model.Orders[0].Order.OrderNo);
                response = new TaskCompletionSource<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>();
                fixture.Api.Search = (r, t) => response.Task;
                pending = fixture.Model.SearchCommand.ExecuteAsync(null);
                fixture.Model.Dispose(); Assert.IsTrue(fixture.Api.LastToken.IsCancellationRequested);
                response.SetResult(Fixture.Success()); await pending;
                Assert.AreEqual(0, fixture.Model.Orders.Count); Assert.IsFalse(fixture.Model.SearchCommand.CanExecute(null));
            }
        }

        /// <summary>单张使用高亮行，批量冻结勾选集合，导出期间禁止重复命令。</summary>
        [TestMethod]
        public async Task ExportSelectionGateAndFrozenInput()
        {
            using (var fixture = new Fixture())
            {
                var model = fixture.Model;
                Assert.IsFalse(model.ExportSelectedCommand.CanExecute(null));
                await model.InitializeAsync(); model.SelectedOrder = model.Orders[0]; model.OutputPath = System.IO.Path.GetTempPath();
                Assert.IsTrue(model.ExportSelectedCommand.CanExecute(null)); Assert.IsFalse(model.ExportCheckedCommand.CanExecute(null));
                var pending = new TaskCompletionSource<PurchaseOrderExportResult>(); fixture.Exporter.Response = () => pending.Task;
                var path = model.OutputPath; var running = model.ExportSelectedCommand.ExecuteAsync(null);
                Assert.IsTrue(model.IsExporting); Assert.IsFalse(model.ExportCheckedCommand.CanExecute(null)); Assert.IsFalse(model.SearchCommand.CanExecute(null));
                model.OutputPath = "C:\\another";
                Assert.AreEqual(path, fixture.Exporter.LastPath); Assert.AreEqual(model.SelectedOrder.Order.PurchaseOrderId, fixture.Exporter.LastIds[0]);
                pending.SetResult(new PurchaseOrderExportResult()); await running;
                Assert.IsFalse(model.IsExporting); Assert.IsFalse(model.IsBusy);
                model.Orders[0].IsChecked = true;
                var other = new SwiftExport.Client.VSTO.Features.Procurement.PurchaseOrders.Models.PurchaseOrderListRow(new PurchaseOrderManagerListItemDto { PurchaseOrderId = Guid.NewGuid() });
                model.Orders.Add(other); model.SelectedOrder = other;
                fixture.Exporter.Response = () => Task.FromResult(new PurchaseOrderExportResult());
                await model.ExportCheckedCommand.ExecuteAsync(null);
                CollectionAssert.AreEqual(new[] { model.Orders[0].Order.PurchaseOrderId }, fixture.Exporter.LastIds);
                model.OutputPath = ""; Assert.IsFalse(model.ExportSelectedCommand.CanExecute(null));
            }
        }

        /// <summary>忽略取消的导出结果不能写入新账号或已销毁的页面。</summary>
        [TestMethod]
        public async Task ExportResultCannotCrossSessionOrDisposal()
        {
            using (var fixture = new Fixture())
            {
                var model = fixture.Model; await model.InitializeAsync(); model.SelectedOrder = model.Orders[0]; model.OutputPath = System.IO.Path.GetTempPath();
                var pending = new TaskCompletionSource<PurchaseOrderExportResult>(); fixture.Exporter.Response = () => pending.Task;
                var running = model.ExportSelectedCommand.ExecuteAsync(null); fixture.Login("second");
                Assert.IsTrue(fixture.Exporter.LastToken.IsCancellationRequested);
                var result = new PurchaseOrderExportResult(); result.Files.Add("old-account.xlsx");
                pending.SetResult(result); await running; Assert.AreEqual("", model.ExportReport);
                await model.InitializeAsync(); model.SelectedOrder = model.Orders[0];
                pending = new TaskCompletionSource<PurchaseOrderExportResult>(); fixture.Exporter.Response = () => pending.Task;
                running = model.ExportSelectedCommand.ExecuteAsync(null); model.Dispose();
                Assert.IsTrue(fixture.Exporter.LastToken.IsCancellationRequested);
                pending.SetResult(result); await running; Assert.AreEqual("", model.ExportReport);
            }
        }

        /// <summary>为纯托管及 STA 场景提供同一个轻量会话与查询替身。</summary>
        internal sealed class Fixture : IDisposable, ITokenStore, IUiDispatcher
        {
            internal readonly QueryApi Api = new QueryApi();
            internal readonly SessionService Session;
            internal readonly ExportService Exporter = new ExportService();
            internal readonly PurchaseOrderListViewModel Model;
            internal Fixture()
            {
                Session = new SessionService(this); Login("first");
                Model = new PurchaseOrderListViewModel(Api, Session, new CurrentUserService(Session), this, new ApiCallRunner(new AddInLog()), Exporter);
            }
            internal void Login(string id, bool allowed = true) => Session.TryCommit(Session.Capture().Version, "test-token",
                new MeDto { UserId = id, UserName = id, Permissions = allowed ? new List<string> { PermissionKeys.ProcurementOrderRead } : new List<string>() });
            internal static ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>> Success(string orderNo = "SO001") =>
                ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>.Ok(new PagedResponseBase<PurchaseOrderManagerListItemDto>
                {
                    TotalCount = 31, PageIndex = 1, PageSize = 30,
                    Items = new List<PurchaseOrderManagerListItemDto> { new PurchaseOrderManagerListItemDto { PurchaseOrderId = Guid.NewGuid(), OrderNo = orderNo, BoNo = "BO001", ReadyQty = 4.25m, ReadyStatus = PurchaseReadyStatusEnum.PartiallyReady, ReceiptStatus = PurchaseReceiptStatusEnum.PartiallyReceived } }
                });
            public string Read() => null;
            public void Write(string token) { }
            public void Clear() { }
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
            public void Dispose() { Model.Dispose(); Session.Dispose(); }
        }
        /// <summary>模拟文件导出入口，记录点击时冻结的订单及路径，可故意返回迟到结果。</summary>
        internal sealed class ExportService : IPurchaseOrderExportService
        {
            internal Guid[] LastIds;
            internal string LastPath;
            internal CancellationToken LastToken;
            internal int Calls;
            internal Func<Task<PurchaseOrderExportResult>> Response = () => Task.FromResult(new PurchaseOrderExportResult());
            public Task<PurchaseOrderExportResult> ExportAsync(IReadOnlyList<Guid> ids, string path, Action<string> progress, CancellationToken token)
            { Calls++; LastIds = System.Linq.Enumerable.ToArray(ids); LastPath = path; LastToken = token; progress?.Invoke("导出中"); return Response(); }
        }
        /// <summary>记录实际传入的共享搜索契约，允许模拟忽略取消的迟到响应。</summary>
        internal sealed class QueryApi : IPurchaseOrderManagerApi
        {
            internal readonly List<PurchaseOrderManagerSearchRequest> Requests = new List<PurchaseOrderManagerSearchRequest>();
            internal CancellationToken LastToken;
            internal Func<PurchaseOrderManagerSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>>> Search =
                (r, t) => Task.FromResult(Fixture.Success());
            public Task<ServiceResult<PagedResponseBase<PurchaseOrderManagerListItemDto>>> SearchAsync(PurchaseOrderManagerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken))
            { Requests.Add(request); LastToken = cancellationToken; return Search(request, cancellationToken); }
            public Task<ServiceResult<List<PurchaseOrderManagerLineDto>>> GetLinesAsync(Guid purchaseOrderId, CancellationToken cancellationToken = default(CancellationToken))
                => throw new NotSupportedException("侧栏只加载单头摘要，不自动展开采购明细。");
        }
    }
}

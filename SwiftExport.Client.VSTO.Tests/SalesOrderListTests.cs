using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Sales.Orders.ViewModels;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Contracts.Apis.SalesOrderApis;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证侧栏真实模型的查询条件、分页与账号生命周期，不加载 Office 或真实服务器。</summary>
    [TestClass]
    public sealed class SalesOrderListTests
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
                model.Orders[0].IsChecked = true; model.SelectedOrder = model.Orders[0];
                await model.InitializeAsync();
                Assert.AreEqual(1, fixture.Api.Requests.Count); Assert.AreEqual(1, model.CheckedCount);
                await model.NextPageCommand.ExecuteAsync(null);
                Assert.AreEqual(2, fixture.Api.Requests[1].PageIndex);
                Assert.AreEqual(0, model.CheckedCount); Assert.IsNull(model.SelectedOrder);
                model.Keyword = " invoice "; model.CustomerCode = " AT "; model.Confirmation = 1;
                model.QuickView = SalesOrderManagerQuickView.PartiallyShipped;
                await model.SearchCommand.ExecuteAsync(null);
                var request = fixture.Api.Requests[2];
                Assert.AreEqual(1, request.PageIndex); Assert.AreEqual("invoice", request.Keyword);
                CollectionAssert.AreEqual(new[] { "AT" }, request.CustomerCodes);
                CollectionAssert.AreEqual(new[] { ConfirmationStatusEnum.已确认 }, request.ConfirmationStatuses);
                Assert.AreEqual(SalesOrderManagerQuickView.PartiallyShipped, request.QuickView);
                Assert.IsNull(model.Orders[0].Order.TotalQty); Assert.AreEqual("未知", model.Orders[0].FulfillmentText);
                Assert.AreEqual("开放", model.Orders[0].DocumentText);
            }
        }

        /// <summary>失败保留已有列表与输入，用户可以主动刷新；无查看权限不发起查询。</summary>
        [TestMethod]
        public async Task FailureRetryAndPermissionGate()
        {
            using (var fixture = new Fixture())
            {
                await fixture.Model.InitializeAsync();
                fixture.Api.Search = (r, t) => Task.FromResult(ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>.Fail("没有权限"));
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
                var response = new TaskCompletionSource<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>>();
                fixture.Api.Search = (r, t) => response.Task;
                var pending = fixture.Model.InitializeAsync();
                Assert.IsTrue(fixture.Model.IsBusy); Assert.IsFalse(fixture.Model.RefreshCommand.CanExecute(null));
                fixture.Login("second");
                Assert.IsTrue(fixture.Api.LastToken.IsCancellationRequested);
                fixture.Api.Search = (r, t) => Task.FromResult(Fixture.Success("new-account"));
                await fixture.Model.RefreshCommand.ExecuteAsync(null);
                response.SetResult(Fixture.Success("old-account")); await pending;
                Assert.AreEqual("new-account", fixture.Model.Orders[0].Order.OrderNo);
                response = new TaskCompletionSource<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>>();
                fixture.Api.Search = (r, t) => response.Task;
                pending = fixture.Model.SearchCommand.ExecuteAsync(null);
                fixture.Model.Dispose(); Assert.IsTrue(fixture.Api.LastToken.IsCancellationRequested);
                response.SetResult(Fixture.Success()); await pending;
                Assert.AreEqual(0, fixture.Model.Orders.Count); Assert.IsFalse(fixture.Model.SearchCommand.CanExecute(null));
            }
        }

        /// <summary>为纯托管及 STA 场景提供同一个轻量会话与查询替身。</summary>
        internal sealed class Fixture : IDisposable, ITokenStore, IUiDispatcher
        {
            internal readonly QueryApi Api = new QueryApi();
            internal readonly SessionService Session;
            internal readonly SalesOrderListViewModel Model;
            internal Fixture()
            {
                Session = new SessionService(this); Login("first");
                Model = new SalesOrderListViewModel(Api, Session, new CurrentUserService(Session), this, new ApiCallRunner(new AddInLog()));
            }
            internal void Login(string id, bool allowed = true) => Session.TryCommit(Session.Capture().Version, "test-token",
                new MeDto { UserId = id, UserName = id, Permissions = allowed ? new List<string> { PermissionKeys.SalesOrderView } : new List<string>() });
            internal static ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>> Success(string orderNo = "SO001") =>
                ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>.Ok(new PagedResponseBase<SalesOrderManagerListItemDto>
                {
                    TotalCount = 31, PageIndex = 1, PageSize = 30,
                    Items = new List<SalesOrderManagerListItemDto> { new SalesOrderManagerListItemDto { Uid = Guid.NewGuid(), OrderNo = orderNo, InvoiceNo = "INV001" } }
                });
            public string Read() => null;
            public void Write(string token) { }
            public void Clear() { }
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
            public void Dispose() { Model.Dispose(); Session.Dispose(); }
        }
        /// <summary>记录实际传入的共享搜索契约，允许模拟忽略取消的迟到响应。</summary>
        internal sealed class QueryApi : ISalesOrderManagerApi
        {
            internal readonly List<SalesOrderManagerSearchRequest> Requests = new List<SalesOrderManagerSearchRequest>();
            internal CancellationToken LastToken;
            internal Func<SalesOrderManagerSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>>> Search =
                (r, t) => Task.FromResult(Fixture.Success());
            public Task<ServiceResult<PagedResponseBase<SalesOrderManagerListItemDto>>> SearchAsync(SalesOrderManagerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken))
            { Requests.Add(request); LastToken = cancellationToken; return Search(request, cancellationToken); }
            public Task<ServiceResult<List<string>>> GetCustomerCodesAsync(CancellationToken cancellationToken = default(CancellationToken))
                => throw new NotSupportedException("客户代码在侧栏手工输入，不额外请求基础资料。");
        }
    }
}

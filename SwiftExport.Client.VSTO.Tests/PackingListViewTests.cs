using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Features.Shipping.PackingLists.ViewModels;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Client.VSTO.Testing;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证装箱单侧栏搜索契约、分页和账号隔离，不访问真实服务器或 Excel。</summary>
    [TestClass]
    public sealed class PackingListViewTests
    {
        /// <summary>首次显示才加载，重复显示保留选择；搜索使用客户 ID 和过账状态，不附加日期条件。</summary>
        [TestMethod]
        public async Task LazyLoadFiltersAndCurrentPageSelection()
        {
            using (var fixture = new Fixture())
            {
                var model = fixture.Model;
                Assert.AreEqual(0, fixture.Api.Requests.Count); Assert.AreEqual(0, fixture.Base.Cache.Loads);
                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), model.OutputPath);
                await model.InitializeAsync();
                var request = fixture.Api.Requests[0]; Assert.AreEqual(30, request.PageSize); Assert.AreEqual(1, request.PageIndex);
                Assert.IsNull(request.CustomerId); Assert.IsNull(request.PostingStatus); Assert.IsNull(request.InvoiceDateFrom); Assert.IsNull(request.InvoiceDateTo);
                model.Documents[0].IsChecked = true; model.SelectedDocument = model.Documents[0];
                await model.InitializeAsync(); Assert.AreEqual(1, fixture.Api.Requests.Count); Assert.AreEqual(1, model.CheckedCount);
                await model.NextPageCommand.ExecuteAsync(null); Assert.AreEqual(2, fixture.Api.Requests[1].PageIndex);
                Assert.AreEqual(0, model.CheckedCount); Assert.IsNull(model.SelectedDocument);
                model.Keyword = " PL-INV "; model.CustomerSelectedCommand.Execute(fixture.Base.Cache.Customers[0]); model.PostingStatus = 8;
                await model.SearchCommand.ExecuteAsync(null); request = fixture.Api.Requests[2];
                Assert.AreEqual("PL-INV", request.Keyword); Assert.AreEqual(1, request.PageIndex);
                Assert.AreEqual(fixture.Base.Cache.Customers[0].Uid, request.CustomerId); Assert.AreEqual(PackingListPostingStatusEnum.Reversed, request.PostingStatus);
                Assert.AreEqual(1, fixture.Base.Cache.Loads); Assert.AreEqual("未过账", model.Documents[0].PostingStatusText);
                model.CustomerClearedCommand.Execute(null); await model.SearchCommand.ExecuteAsync(null); Assert.IsNull(fixture.Api.Requests[3].CustomerId);
            }
        }

        /// <summary>客户缓存失败仍能查询，刷新重试资料；列表失败保留输入和已有行，无查看权限不查询。</summary>
        [TestMethod]
        public async Task CacheFailureQueryRetryAndPermissionGate()
        {
            using (var fixture = new Fixture())
            {
                fixture.Base.Cache.Load = () => Task.FromResult(CacheRefreshResult.Fail("资料失败"));
                await fixture.Model.InitializeAsync(); Assert.AreEqual(1, fixture.Model.Documents.Count);
                StringAssert.Contains(fixture.Model.CacheMessage, "资料失败"); Assert.IsNull(fixture.Api.Requests[0].CustomerId);
                fixture.Base.Cache.Load = () => Task.FromResult(CacheRefreshResult.Ok());
                await fixture.Model.RefreshCommand.ExecuteAsync(null); Assert.AreEqual(2, fixture.Base.Cache.Loads); Assert.AreEqual("", fixture.Model.CacheMessage);
                var row = fixture.Model.Documents[0]; row.IsChecked = true; fixture.Model.Keyword = "keep";
                fixture.Api.Search = (r, t) => Task.FromResult(ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>.Fail("查询失败"));
                await fixture.Model.SearchCommand.ExecuteAsync(null);
                Assert.AreSame(row, fixture.Model.Documents[0]); Assert.AreEqual(1, fixture.Model.CheckedCount);
                Assert.AreEqual("keep", fixture.Model.Keyword); Assert.AreEqual("查询失败", fixture.Model.ErrorMessage); Assert.IsFalse(fixture.Model.IsBusy);
                fixture.Login("no-access", false); Assert.AreEqual(0, fixture.Model.Documents.Count); Assert.IsNull(fixture.Model.SelectedCustomer);
                Assert.IsFalse(fixture.Model.SearchCommand.CanExecute(null)); var calls = fixture.Api.Requests.Count;
                await fixture.Model.InitializeAsync(); Assert.AreEqual(calls, fixture.Api.Requests.Count);
            }
        }

        /// <summary>忽略取消的查询结果不能跨账号或回填已释放页面；取消旧请求不影响新查询。</summary>
        [TestMethod]
        public async Task LateQueryCannotCrossSessionOrDisposal()
        {
            using (var fixture = new Fixture())
            {
                var response = new TaskCompletionSource<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>();
                fixture.Api.Search = (r, t) => response.Task;
                var pending = fixture.Model.InitializeAsync(); Assert.IsTrue(fixture.Model.IsBusy);
                fixture.Login("second"); Assert.IsTrue(fixture.Api.LastToken.IsCancellationRequested);
                fixture.Api.Search = (r, t) => Task.FromResult(Success("new-account"));
                await fixture.Model.RefreshCommand.ExecuteAsync(null);
                response.SetResult(Success("old-account")); await pending;
                Assert.AreEqual("new-account", fixture.Model.Documents[0].Document.PackingListNo);
                response = new TaskCompletionSource<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>();
                fixture.Api.Search = (r, t) => response.Task;
                pending = fixture.Model.SearchCommand.ExecuteAsync(null); fixture.Model.Dispose();
                Assert.IsTrue(fixture.Api.LastToken.IsCancellationRequested); response.SetResult(Success()); await pending;
                Assert.AreEqual(0, fixture.Model.Documents.Count); Assert.IsFalse(fixture.Model.RefreshCommand.CanExecute(null));
            }
        }

        /// <summary>客户加载无法取消时仍核对请求身份，旧缓存失败不覆盖新账号列表和提示。</summary>
        [TestMethod]
        public async Task LateCacheCannotStartOldAccountQuery()
        {
            using (var fixture = new Fixture())
            {
                var response = new TaskCompletionSource<CacheRefreshResult>(); fixture.Base.Cache.Load = () => response.Task;
                var pending = fixture.Model.InitializeAsync(); Assert.AreEqual(0, fixture.Api.Requests.Count);
                fixture.Login("second"); fixture.Base.Cache.Load = () => Task.FromResult(CacheRefreshResult.Ok());
                await fixture.Model.InitializeAsync(); Assert.AreEqual(1, fixture.Api.Requests.Count);
                response.SetResult(CacheRefreshResult.Fail("旧缓存失败")); await pending;
                Assert.AreEqual(1, fixture.Api.Requests.Count); Assert.AreEqual("", fixture.Model.CacheMessage); Assert.AreEqual(1, fixture.Model.Documents.Count);
            }
        }

        /// <summary>真实 Refit 搜索沿用已有路径与认证头，未创建单独的装箱单列表接口。</summary>
        [TestMethod]
        public async Task SearchUsesExistingAuthenticatedRoute()
        {
            using (var fixture = new Fixture())
            using (var clients = new ApiClientRegistration(new Uri("https://test.invalid/"), fixture.Base.Session, new QueryTransport(), new QueryTransport()))
            {
                var api = clients.Create<IPackingListApi>("api/shipping/packing-lists");
                var result = await api.SearchAsync(new PackingListManagerSearchRequest { Keyword = "route", PageSize = 30 });
                Assert.IsTrue(result.Success); Assert.AreEqual("PL-ROUTE", result.Data.Items[0].PackingListNo);
            }
        }

        /// <summary>为纯托管和 STA 测试共用实际页面模型、客户缓存及验证会话。</summary>
        internal sealed class Fixture : IDisposable
        {
            internal readonly ImportTestFixture Base = new ImportTestFixture();
            internal readonly QueryApi Api = new QueryApi();
            internal readonly PackingListViewModel Model;
            internal Fixture()
            {
                Login("first"); Model = new PackingListViewModel(Api, Base.Session, new CurrentUserService(Base.Session), Base, Base.Cache, new ApiCallRunner(new AddInLog()));
            }
            internal void Login(string id, bool allowed = true) => Base.Session.TryCommit(Base.Session.Capture().Version, "test-token",
                new MeDto { UserId = id, UserName = id, Permissions = allowed ? new List<string> { PermissionKeys.ShippingPackingListView } : new List<string>() });
            /// <summary>释放自有页面和会话，测试缓存由夹具持有。</summary>
            public void Dispose() { Model.Dispose(); Base.Dispose(); }
        }
        internal static ServiceResult<PagedResponseBase<PackingListManagerListItemDto>> Success(string number = "PL001") =>
            ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>.Ok(new PagedResponseBase<PackingListManagerListItemDto>
            {
                TotalCount = 31, PageIndex = 1, PageSize = 30,
                Items = new List<PackingListManagerListItemDto> { new PackingListManagerListItemDto
                { Uid = Guid.NewGuid(), PackingListNo = number, CustomerNameSnapshot = "客户快照", InvoiceDate = new DateTime(2026, 10, 11),
                    InvoiceNosSummary = "260820;260821", ContainerNo = "CONT1", SealNo = "SEAL1", Etd = new DateTime(2026, 10, 12) } }
            });

        /// <summary>只支持摘要查询，其他业务命令明确拒绝，避免列表意外承担写入职责。</summary>
        internal sealed class QueryApi : IPackingListApi
        {
            internal readonly List<PackingListManagerSearchRequest> Requests = new List<PackingListManagerSearchRequest>();
            internal CancellationToken LastToken;
            internal Func<PackingListManagerSearchRequest, CancellationToken, Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>>> Search =
                (r, t) => Task.FromResult(Success());
            /// <summary>记录真实请求和取消令牌，支持模拟迟到结果。</summary>
            public Task<ServiceResult<PagedResponseBase<PackingListManagerListItemDto>>> SearchAsync(PackingListManagerSearchRequest request, CancellationToken cancellationToken = default(CancellationToken))
            { Requests.Add(request); LastToken = cancellationToken; return Search(request, cancellationToken); }
            /// <inheritdoc />
            public Task<ServiceResult<PackingListDraftCommandResult>> AssociateSourcesAsync(Guid uid, Guid lineUid, AssociatePackingListSourcesCommand request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListPostingPreviewDto>> PostPreviewAsync(Guid uid, PackingListPostPreviewRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListPostingCommandResult>> PostAsync(Guid uid, PostPackingListCommand request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListPostingPreviewDto>> ReversePreviewAsync(Guid uid, PackingListReversePreviewRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListPostingCommandResult>> ReverseAsync(Guid uid, ReversePackingListCommand request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListTraceDto>> TraceAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListDetailDto>> GetAsync(Guid uid, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PagedResponseBase<PackingListSourceCandidateDto>>> SearchSourcesAsync(PackingListSourceSearchRequest request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListDraftCommandResult>> CreateAsync(CreatePackingListDraftCommand request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(Guid uid, UpdatePackingListDraftCommand request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
            /// <inheritdoc />
            public Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(Guid uid, DeletePackingListDraftCommand request, CancellationToken cancellationToken = default(CancellationToken)) => throw new NotSupportedException();
        }
        /// <summary>验证真实 HTTP 路径和请求身份，返回内存摘要。</summary>
        private sealed class QueryTransport : System.Net.Http.HttpMessageHandler
        {
            /// <summary>仅接受搜索路由，不访问外部网络。</summary>
            protected override Task<System.Net.Http.HttpResponseMessage> SendAsync(System.Net.Http.HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Assert.AreEqual("/api/shipping/packing-lists/search", request.RequestUri.AbsolutePath);
                Assert.AreEqual("Bearer", request.Headers.Authorization.Scheme); Assert.AreEqual("test-token", request.Headers.Authorization.Parameter);
                return Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.StringContent(System.Text.Json.JsonSerializer.Serialize(Success("PL-ROUTE")), System.Text.Encoding.UTF8, "application/json") });
            }
        }
    }
}

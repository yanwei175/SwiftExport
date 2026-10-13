using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Services.Caching;
using SwiftExport.Contracts.Apis.CustomerApis;
using SwiftExport.Contracts.Apis.SupplierApis;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.ProductApis;
using SwiftExport.Contracts.Apis.SystemApis;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>通过真实 Refit 代理和模拟传输验证缓存；不连接服务器或启动 Excel。</summary>
    [TestClass]
    public sealed class ReferenceDataCacheTests
    {
        /// <summary>旧映射退出基础资料缓存，既不暴露映射操作，也不依赖旧映射 API。</summary>
        [TestMethod, Timeout(10000)]
        public async Task LegacyMappingIsNotPartOfReferenceDataCache()
        {
            Assert.IsFalse(typeof(IReferenceDataCacheService).GetMethods().Any(method => method.Name.Contains("ExcelFieldMappings")));
            Assert.IsFalse(typeof(ReferenceDataCacheService).GetConstructors().SelectMany(constructor => constructor.GetParameters())
                .Any(parameter => parameter.ParameterType.Name == "IExcelFieldMappingApi"));
            using (var f = new Fixture())
            {
                Assert.IsTrue((await f.Cache.EnsureLoadedAsync()).Success);
                Assert.IsFalse(f.Transport.Calls.Any(path => path.StartsWith("/api/excel-field-mappings", StringComparison.Ordinal)));
            }
        }

        /// <summary>创建缓存、登录和换号都不预加载，未认证使用不请求服务器。</summary>
        [TestMethod, Timeout(10000)]
        public async Task ConstructionAndSessionChangesDoNotLoad()
        {
            using (var f = new Fixture(false))
            {
                Assert.IsFalse((await f.Cache.EnsureLoadedAsync()).Success);
                Assert.IsFalse((await f.Cache.RefreshCustomersAsync()).Success);
                f.Login("one");
                f.Login("two");
                Assert.AreEqual(0, f.Transport.Calls.Count);
                Assert.AreEqual(0, f.Cache.Customers.Count);
            }
        }

        /// <summary>并发首次加载共用任务，覆盖全部基础资料，仓库保持独立按需加载。</summary>
        [TestMethod, Timeout(10000)]
        public async Task FirstLoadCoalescesAndLoadsFullScope()
        {
            using (var f = new Fixture())
            {
                var gate = new TaskCompletionSource<HttpResponseMessage>();
                f.Transport.Override = (path, request) => path == "/api/ProductColor/all" ? gate.Task : null;
                var first = f.Cache.EnsureLoadedAsync();
                var second = f.Cache.EnsureLoadedAsync();
                Assert.AreSame(first, second);
                gate.SetResult(Ok(new[] { new { colorCode = "RED" } }));
                Assert.IsTrue((await first).Success);
                Assert.IsTrue((await second).Success);
                Assert.IsTrue((await f.Cache.EnsureLoadedAsync()).Success);
                Assert.AreEqual(7, f.Transport.Calls.Count);
                Assert.AreEqual("RED", f.Cache.ProductColors.Single().ColorCode);
                Assert.AreEqual("C001", f.Cache.Customers.Single().CustomerCode);
                Assert.AreEqual("S001", f.Cache.Suppliers.Single().SupplierCode);
                Assert.AreEqual("USD", f.Cache.Currencies.Single().CurrencyCode);
                Assert.AreEqual(1, f.Cache.ProductCategories.Count);
                Assert.AreSame(f.Cache.ProductCategoryTree[0], f.Cache.ProductCategories[0]);
                Assert.AreEqual(1, f.Cache.DictionaryTypes.Count);
                Assert.AreEqual(2, f.Cache.DictionaryValues.Count);
                Assert.AreEqual(1, f.Cache.GetDictionaryValues(" unit ").Count);
                Assert.AreEqual(2, f.Cache.GetDictionaryValues("UNIT", false).Count);
                Assert.AreEqual(0, f.Cache.WarehouseSites.Count);
                Assert.IsTrue((await f.Cache.EnsureWarehouseSitesLoadedAsync()).Success);
                Assert.IsTrue((await f.Cache.EnsureWarehouseSitesLoadedAsync()).Success);
                Assert.AreEqual(8, f.Transport.Calls.Count);
                Assert.AreEqual("W001", f.Cache.WarehouseSites.Single().Code);
                Assert.IsTrue(f.Transport.Tokens.All(token => token == "one"));
            }
        }

        /// <summary>刷新不替换集合实例；网络失败保留原快照且后续能够重试。</summary>
        [TestMethod, Timeout(10000)]
        public async Task RefreshPreservesBindingsAndFailureKeepsSnapshot()
        {
            using (var f = new Fixture())
            {
                var collection = f.Cache.ProductColors;
                var resets = 0;
                ((INotifyCollectionChanged)collection).CollectionChanged += (s, e) => resets++;
                Assert.IsTrue((await f.Cache.RefreshProductColorsAsync()).Success);
                f.Transport.Override = (p, r) => Task.FromException<HttpResponseMessage>(new HttpRequestException());
                Assert.IsFalse((await f.Cache.RefreshProductColorsAsync()).Success);
                Assert.AreEqual("OLD", collection.Single().ColorCode);
                Assert.AreEqual(1, resets);
                f.Transport.Override = (p, r) => Task.FromResult(Ok(new[] { new { colorCode = "NEW" } }));
                Assert.IsTrue((await f.Cache.RefreshProductColorsAsync()).Success);
                Assert.AreSame(collection, f.Cache.ProductColors);
                Assert.AreEqual("NEW", collection.Single().ColorCode);
                Assert.AreEqual(2, resets);
                Assert.AreEqual(2, f.Dispatcher.Calls);
            }
        }

        /// <summary>全量刷新任一资料失败都不发布其他已成功的新资料。</summary>
        [TestMethod, Timeout(10000)]
        public async Task FullRefreshAndDictionaryRefreshAreAllOrNothing()
        {
            using (var f = new Fixture())
            {
                Assert.IsTrue((await f.Cache.EnsureLoadedAsync()).Success);
                f.Transport.Override = (p, r) =>
                {
                    if (p == "/api/ProductColor/all") return Task.FromResult(Ok(new[] { new { colorCode = "NEW" } }));
                    if (p == "/api/SysDictType/all") return Task.FromResult(Ok(new[] { new { dictCode = "NEW" } }));
                    if (p == "/api/SysDictData/all") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                    return null;
                };
                Assert.IsFalse((await f.Cache.RefreshAllAsync()).Success);
                Assert.AreEqual("OLD", f.Cache.ProductColors.Single().ColorCode);
                Assert.IsFalse((await f.Cache.RefreshDictionariesAsync()).Success);
                Assert.AreEqual("UNIT", f.Cache.DictionaryTypes.Single().DictCode);
                Assert.AreEqual("one", f.Session.Capture().Token);
            }
        }

        /// <summary>三类分页资料读取所有页，后页失败不覆盖已发布集合。</summary>
        [TestMethod, Timeout(10000)]
        public async Task PagedCachesRequireEveryPage()
        {
            using (var f = new Fixture())
            {
                foreach (var kind in new[] { "Customer", "Supplier", "inventory/warehouse-sites" })
                {
                    var count = 0;
                    var fail = false;
                    f.Transport.Override = (p, r) =>
                    {
                        if (!p.Contains("/" + kind + "/")) return null;
                        count++;
                        if (count == 2 && fail) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
                        return Task.FromResult(Ok(new
                        {
                            items = new[] { new { customerCode = "C" + count, supplierCode = "S" + count, code = "W" + count } },
                            totalCount = 2
                        }));
                    };
                    Func<Task<CacheRefreshResult>> refresh = kind == "Customer"
                        ? (Func<Task<CacheRefreshResult>>)f.Cache.RefreshCustomersAsync
                        : kind == "Supplier" ? (Func<Task<CacheRefreshResult>>)f.Cache.RefreshSuppliersAsync : f.Cache.RefreshWarehouseSitesAsync;
                    Assert.IsTrue((await refresh()).Success);
                    Assert.AreEqual(2, count);
                    count = 0;
                    fail = true;
                    Assert.IsFalse((await refresh()).Success);
                    Assert.AreEqual(2, count);
                }
                Assert.AreEqual(2, f.Cache.Customers.Count);
                Assert.AreEqual(2, f.Cache.Suppliers.Count);
                Assert.AreEqual(2, f.Cache.WarehouseSites.Count);
            }
        }

        /// <summary>空分页提前结束与成功但缺失数据都作为失败，不能把旧缓存清空。</summary>
        [TestMethod, Timeout(10000)]
        public async Task MalformedResponsesKeepOldSnapshot()
        {
            using (var f = new Fixture())
            {
                Assert.IsTrue((await f.Cache.RefreshCustomersAsync()).Success);
                var count = 0;
                f.Transport.Override = (p, r) => Task.FromResult(++count == 1
                    ? Ok(new { items = new[] { new { customerCode = "NEW" } }, totalCount = 3 })
                    : Ok(new { items = new object[0], totalCount = 3 }));
                Assert.IsFalse((await f.Cache.RefreshCustomersAsync()).Success);
                Assert.AreEqual("C001", f.Cache.Customers.Single().CustomerCode);
                f.Transport.Override = (p, r) => Task.FromResult(Ok(null));
                Assert.IsFalse((await f.Cache.RefreshCustomersAsync()).Success);
                Assert.AreEqual("C001", f.Cache.Customers.Single().CustomerCode);
            }
        }

        /// <summary>退出清理所有资料；再次使用必须重新登录且不得复用旧加载标记。</summary>
        [TestMethod, Timeout(10000)]
        public async Task LogoutClearsAndNextSessionReloads()
        {
            using (var f = new Fixture())
            {
                Assert.IsTrue((await f.Cache.EnsureLoadedAsync()).Success);
                Assert.IsTrue((await f.Cache.EnsureWarehouseSitesLoadedAsync()).Success);
                f.Session.TryEnd(f.Session.Capture().Version);
                Assert.AreEqual(0, f.Cache.Customers.Count);
                Assert.AreEqual(0, f.Cache.WarehouseSites.Count);
                Assert.AreEqual(0, f.Cache.DictionaryValues.Count);
                Assert.IsFalse((await f.Cache.EnsureLoadedAsync()).Success);
                Assert.AreEqual(8, f.Transport.Calls.Count);
                f.Login("two");
                Assert.AreEqual(8, f.Transport.Calls.Count);
                Assert.IsTrue((await f.Cache.EnsureLoadedAsync()).Success);
                Assert.AreEqual(15, f.Transport.Calls.Count);
            }
        }

        /// <summary>手动清理、换号或释放后，已发出的成功响应都不能回填缓存。</summary>
        [TestMethod, Timeout(10000)]
        public async Task LateSuccessCannotSurviveClearSwitchOrDisposal()
        {
            foreach (var transition in new[] { "clear", "switch", "dispose" })
            {
                using (var f = new Fixture())
                {
                    var gate = new TaskCompletionSource<HttpResponseMessage>();
                    f.Transport.Override = (p, r) => gate.Task;
                    var pending = f.Cache.RefreshProductColorsAsync();
                    if (transition == "clear") f.Cache.Clear();
                    else if (transition == "switch") f.Login("two");
                    else f.Cache.Dispose();
                    gate.SetResult(Ok(new[] { new { colorCode = "LATE" } }));
                    Assert.IsFalse((await pending).Success, transition);
                    Assert.AreEqual(0, f.Cache.ProductColors.Count, transition);
                    if (transition == "dispose")
                    {
                        Assert.IsFalse((await f.Cache.EnsureLoadedAsync()).Success);
                        Assert.IsFalse((await f.Cache.RefreshProductColorsAsync()).Success);
                        Assert.AreEqual(1, f.Transport.Calls.Count);
                    }
                }
            }
        }

        /// <summary>旧首次加载完成不能解除新账号的并发门禁，排队请求采用创建时身份。</summary>
        [TestMethod, Timeout(10000)]
        public async Task SwitchingDuringEnsureKeepsNewTaskCoalesced()
        {
            using (var f = new Fixture())
            {
                var gate = new TaskCompletionSource<HttpResponseMessage>();
                var colors = 0;
                f.Transport.Override = (p, r) => p == "/api/ProductColor/all" && ++colors == 1 ? gate.Task : null;
                var old = f.Cache.EnsureLoadedAsync();
                var oldQueued = f.Cache.RefreshProductColorsAsync();
                f.Login("two");
                var current = f.Cache.EnsureLoadedAsync();
                Assert.AreSame(current, f.Cache.EnsureLoadedAsync());
                gate.SetResult(Ok(new[] { new { colorCode = "LATE" } }));
                Assert.IsFalse((await old).Success);
                Assert.IsFalse((await oldQueued).Success);
                Assert.IsTrue((await current).Success);
                Assert.AreEqual(2, colors);
                Assert.AreEqual("OLD", f.Cache.ProductColors.Single().ColorCode);
            }
        }

        /// <summary>当前账号 401 清理身份和缓存，旧账号 401 不影响新登录。</summary>
        [TestMethod, Timeout(10000)]
        public async Task UnauthorizedOnlyInvalidatesMatchingSession()
        {
            using (var f = new Fixture())
            {
                Assert.IsTrue((await f.Cache.EnsureLoadedAsync()).Success);
                var gate = new TaskCompletionSource<HttpResponseMessage>();
                f.Transport.Override = (p, r) => gate.Task;
                var pending = f.Cache.RefreshProductColorsAsync();
                f.Login("two");
                gate.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                Assert.IsFalse((await pending).Success);
                Assert.AreEqual("two", f.Session.Capture().Token);
                f.Transport.Override = (p, r) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                Assert.IsFalse((await f.Cache.RefreshProductColorsAsync()).Success);
                Assert.IsNull(f.Session.Capture().User);
                Assert.AreEqual(0, f.Cache.ProductColors.Count);
            }
        }

        /// <summary>集合发布必须等待 UI 调度，进入队列后切换账号也不能发布。</summary>
        [TestMethod, Timeout(10000)]
        public async Task QueuedPublicationRechecksSession()
        {
            using (var f = new Fixture())
            {
                f.Dispatcher.Hold = true;
                var pending = f.Cache.RefreshProductColorsAsync();
                await f.Dispatcher.Queued.Task;
                Assert.AreEqual(0, f.Cache.ProductColors.Count);
                f.Login("two");
                f.Dispatcher.Drain();
                Assert.IsFalse((await pending).Success);
                Assert.AreEqual(0, f.Cache.ProductColors.Count);
            }
        }

        /// <summary>分类树修复孤立和循环父链，扁平列表共享节点并以显示名称生成路径。</summary>
        [TestMethod, Timeout(10000)]
        public async Task CategoriesPreserveTreeAndRepairCycles()
        {
            using (var f = new Fixture())
            {
                var root = Guid.NewGuid(); var child = Guid.NewGuid();
                var a = Guid.NewGuid(); var b = Guid.NewGuid(); var orphan = Guid.NewGuid();
                f.Transport.Override = (p, r) => Task.FromResult(Ok(new[]
                {
                    new { uid = root, parentId = (Guid?)null, categoryName = "ROOT", fakeName = "根分类", sortNo = 1 },
                    new { uid = child, parentId = (Guid?)root, categoryName = "CHILD", fakeName = "子分类", sortNo = 2 },
                    new { uid = a, parentId = (Guid?)b, categoryName = "A", fakeName = "循环甲", sortNo = 3 },
                    new { uid = b, parentId = (Guid?)a, categoryName = "B", fakeName = "循环乙", sortNo = 4 },
                    new { uid = orphan, parentId = (Guid?)Guid.NewGuid(), categoryName = "ORPHAN", fakeName = "孤立分类", sortNo = 5 }
                }));
                Assert.IsTrue((await f.Cache.RefreshProductCategoriesAsync()).Success);
                Assert.AreEqual(4, f.Cache.ProductCategoryTree.Count);
                Assert.AreEqual(5, f.Cache.ProductCategories.Count);
                Assert.AreEqual("根分类", f.Cache.ProductCategoryTree[0].CategoryPath);
                Assert.AreEqual("根分类 / 子分类", f.Cache.ProductCategories.Single(x => x.Uid == child).CategoryPath);
                Assert.AreSame(f.Cache.ProductCategoryTree[0].Children[0], f.Cache.ProductCategories[1]);
            }
        }

        /// <summary>构造与真实接口相同的 JSON 包装。</summary>
        private static HttpResponseMessage Ok(object data)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { success = true, data }), Encoding.UTF8, "application/json")
            };
        }

        /// <summary>统一创建认证管道及全部八类真实 Refit 代理。</summary>
        private sealed class Fixture : IDisposable
        {
            public readonly SessionService Session = new SessionService(new MemoryStore());
            public readonly Transport Transport = new Transport();
            public readonly Dispatcher Dispatcher = new Dispatcher();
            public readonly ReferenceDataCacheService Cache;
            private readonly ApiClientRegistration registration;
            /// <summary>创建按需缓存，按测试需要建立初始身份。</summary>
            public Fixture(bool login = true)
            {
                if (login) Login("one");
                registration = new ApiClientRegistration(new Uri("https://example.test/"), Session, new Transport(), Transport);
                Cache = new ReferenceDataCacheService(
                    registration.Create<ICustomerApi>("api/Customer"),
                    registration.Create<ISupplierApi>("api/Supplier"),
                    registration.Create<IWarehouseSiteApi>("api/inventory/warehouse-sites"),
                    registration.Create<IProductColorApi>("api/ProductColor"),
                    registration.Create<IProductCategoryApi>("api/ProductCategory"),
                    registration.Create<ISysDicCurrencyApi>("api/SysDicCurrency"),
                    registration.Create<ISysDictTypeApi>("api/SysDictType"),
                    registration.Create<ISysDictDataApi>("api/SysDictData"),
                    new ApiCallRunner(new AddInLog()), Session, Dispatcher);
            }
            /// <summary>模拟已经过后端验证的身份提交。</summary>
            public void Login(string name)
            {
                Assert.IsTrue(Session.TryCommit(Session.Capture().Version, name, new MeDto { UserId = Guid.NewGuid().ToString(), UserName = name }));
            }
            /// <summary>按宿主拥有关系释放缓存及连接。</summary>
            public void Dispose() { Cache.Dispose(); registration.Dispose(); Session.Dispose(); }
        }

        /// <summary>只保留内存令牌，避免测试接触真实用户的 DPAPI 存储。</summary>
        private sealed class MemoryStore : ITokenStore
        {
            private string token;
            /// <summary>读取测试令牌。</summary>
            public string Read() => token;
            /// <summary>替换测试令牌。</summary>
            public void Write(string value) { token = value; }
            /// <summary>清除测试令牌。</summary>
            public void Clear() { token = null; }
        }

        /// <summary>可控 UI 调度队列，用于验证必须经调度器发布及排队后的二次校验。</summary>
        private sealed class Dispatcher : IUiDispatcher
        {
            public int Calls;
            public bool Hold;
            public readonly TaskCompletionSource<bool> Queued = new TaskCompletionSource<bool>();
            private readonly Queue<Action> actions = new Queue<Action>();
            /// <summary>立即执行或由测试保留待执行动作。</summary>
            public Task InvokeAsync(Action action)
            {
                Calls++;
                if (!Hold) { action(); return Task.CompletedTask; }
                var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                actions.Enqueue(() => { action(); completion.SetResult(true); });
                Queued.TrySetResult(true);
                return completion.Task;
            }
            /// <summary>按进入顺序执行 UI 操作并解除阻塞。</summary>
            public void Drain()
            {
                Hold = false;
                while (actions.Count > 0) actions.Dequeue()();
            }
        }

        /// <summary>校验实际路由并返回独立资料快照；允许测试控制迟到响应和失败。</summary>
        private sealed class Transport : HttpMessageHandler
        {
            public readonly List<string> Calls = new List<string>();
            public readonly List<string> Tokens = new List<string>();
            public Func<string, HttpRequestMessage, Task<HttpResponseMessage>> Override;
            /// <summary>记录真实请求地址和授权头，返回测试准备的响应。</summary>
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                var path = request.RequestUri.AbsolutePath;
                Calls.Add(path);
                Tokens.Add(request.Headers.Authorization?.Parameter);
                var response = Override?.Invoke(path, request);
                if (response != null) return response;
                object data;
                switch (path)
                {
                    case "/api/ProductColor/all": data = new[] { new { colorCode = "OLD" } }; break;
                    case "/api/ProductCategory/all": data = new[] { new { uid = Guid.NewGuid(), categoryName = "ROOT" } }; break;
                    case "/api/Customer/page": data = new { items = new[] { new { customerCode = "C001" } }, totalCount = 1 }; break;
                    case "/api/Supplier/page": data = new { items = new[] { new { supplierCode = "S001" } }, totalCount = 1 }; break;
                    case "/api/inventory/warehouse-sites/picker": data = new { items = new[] { new { code = "W001" } }, totalCount = 1 }; break;
                    case "/api/SysDicCurrency/enabled": data = new[] { new { currencyCode = "USD", isEnabled = true }, new { currencyCode = "OFF", isEnabled = false } }; break;
                    case "/api/SysDictType/all": data = new[] { new { dictCode = "UNIT" } }; break;
                    case "/api/SysDictData/all": data = new[] { new { dictCode = "UNIT", isEnabled = true }, new { dictCode = "UNIT", isEnabled = false } }; break;
                    default: throw new InvalidOperationException("未预期的 API 路由：" + path);
                }
                return Task.FromResult(Ok(data));
            }
        }
    }
}

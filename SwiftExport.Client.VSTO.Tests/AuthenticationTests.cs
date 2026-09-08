using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Contracts.DTOs.Auth;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证候选认证与会话隔离，不加载 Excel 或 VSTO。</summary>
    [TestClass]
    public sealed class AuthenticationTests
    {
        /// <summary>成功验证用户后才替换旧账号并持久化。</summary>
        [TestMethod]
        public async Task LoginCommitsOnlyAfterMe()
        {
            var store = new MemoryStore(); var session = new SessionService(store);
            session.TryCommit(0, "old", User("old"));
            var gateway = new Gateway();
            var service = new AuthService(gateway, session, store);
            var task = service.LoginAsync("new", "password", CancellationToken.None);
            Assert.AreEqual("old", session.Capture().Token);
            gateway.Me.SetResult(User("new"));
            Assert.IsTrue(await task);
            Assert.AreEqual("new-token", store.Token);
            Assert.AreEqual("new", session.Capture().User.UserName);
        }
        /// <summary>候选请求失败不清除原账号。</summary>
        [TestMethod]
        public async Task CandidateFailurePreservesOldAccount()
        {
            var store = new MemoryStore(); var session = new SessionService(store); session.TryCommit(0, "old", User("old"));
            var gateway = new Gateway(); gateway.Me.SetException(new HttpRequestException());
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new AuthService(gateway, session, store).LoginAsync("new", "password", CancellationToken.None));
            Assert.AreEqual("old", session.Capture().Token);
        }
        /// <summary>取消窗口后，即使 /me 成功也不提交身份。</summary>
        [TestMethod]
        public async Task CancelPreservesOldAccount()
        {
            var store = new MemoryStore(); var session = new SessionService(store); session.TryCommit(0, "old", User("old"));
            var gateway = new Gateway(); var cts = new CancellationTokenSource();
            var task = new AuthService(gateway, session, store).LoginAsync("new", "password", cts.Token);
            cts.Cancel(); gateway.Me.SetResult(User("new"));
            await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => task);
            Assert.AreEqual("old", store.Token);
        }
        /// <summary>网络不可达不会删除持久化登录。</summary>
        [TestMethod]
        public async Task RestoreNetworkFailureKeepsCache()
        {
            var store = new MemoryStore { Token = "cached" }; var session = new SessionService(store);
            var gateway = new Gateway(); gateway.Me.SetException(new HttpRequestException());
            await Assert.ThrowsExactlyAsync<HttpRequestException>(() => new AuthService(gateway, session, store).RestoreAsync(CancellationToken.None));
            Assert.AreEqual("cached", store.Token); Assert.IsNull(session.Capture().User);
        }
        /// <summary>旧请求的 401 不得退出已切换的新账号。</summary>
        [TestMethod]
        public async Task LateUnauthorizedCannotEndNewSession()
        {
            var store = new MemoryStore(); var session = new SessionService(store); session.TryCommit(0, "old", User("old"));
            var transport = new DeferredHandler();
            using (var client = new HttpClient(new AuthTokenHandler(session) { InnerHandler = transport }))
            {
                var request = client.GetAsync("https://example.test/data");
                Assert.AreEqual("old", transport.Token);
                session.TryCommit(session.Capture().Version, "new", User("new"));
                transport.Response.SetResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
                await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => request);
                Assert.AreEqual("new", session.Capture().Token);
            }
        }
        /// <summary>退出清空缓存，释放后不能重新接纳身份。</summary>
        [TestMethod]
        public void LogoutAndDisposalInvalidateOperations()
        {
            var store = new MemoryStore(); var session = new SessionService(store); session.TryCommit(0, "old", User("old"));
            Assert.IsTrue(session.TryEnd(session.Capture().Version)); Assert.IsNull(store.Token);
            var before = session.Capture(); session.Dispose();
            Assert.IsFalse(session.TryCommit(before.Version, "late", User("late")));
        }
        /// <summary>直接执行命令也不能重复发送登录，释放后不回写 UI。</summary>
        [TestMethod]
        public async Task ViewModelGuardsDuplicateAndDisposedCompletion()
        {
            var store = new MemoryStore(); var session = new SessionService(store); var gateway = new Gateway();
            var model = new LoginViewModel(new AuthService(gateway, session, store), new ApiCallRunner(new AddInLog()), new InlineDispatcher()) { UserName = "user" };
            var accepted = 0; model.Succeeded += (sender, args) => accepted++;
            var first = model.LoginCommand.ExecuteAsync("password");
            await model.LoginCommand.ExecuteAsync("password");
            Assert.AreEqual(1, gateway.LoginCount);
            model.Dispose(); gateway.Me.SetResult(User("user")); await first;
            Assert.AreEqual(0, accepted); Assert.IsNull(session.Capture().User);
        }
        /// <summary>Ribbon 定义的回调和唯一控件 ID 必须能对应实现。</summary>
        [TestMethod]
        public void RibbonCallbacksAndIdsMatch()
        {
            var directory = Path.GetDirectoryName(typeof(AuthenticationTests).Assembly.Location);
            var xml = XDocument.Load(Path.Combine(directory, "SwiftExportRibbon.xml"));
            var source = File.ReadAllText(Path.Combine(directory, "SwiftExportRibbon.cs"));
            var ids = xml.Descendants().Attributes("id").Select(a => a.Value).ToArray();
            Assert.AreEqual(ids.Length, ids.Distinct().Count());
            foreach (var attribute in xml.Descendants().Attributes().Where(a => a.Name.LocalName.StartsWith("get") || a.Name.LocalName == "onAction" || a.Name.LocalName == "onLoad"))
                StringAssert.Contains(source, attribute.Value + "(");
        }
        private static MeDto User(string name) { return new MeDto { UserId = name, UserName = name }; }
        /// <summary>真实 Refit 代理可在 net48 消费共享 Contracts，候选头互不污染。</summary>
        [TestMethod]
        public async Task RefitProxyUsesPerCandidateAuthorization()
        {
            var store = new MemoryStore(); var session = new SessionService(store); session.TryCommit(0, "current", User("current"));
            var transport = new AuthTransport();
            using (var api = new ApiClientRegistration(new Uri("https://example.test"), session, transport))
            {
                Assert.AreEqual("candidate", await api.LoginAsync("name", "password"));
                var users = await Task.WhenAll(api.GetMeAsync("one"), api.GetMeAsync("two"));
                Assert.AreEqual("one", users[0].UserName);
                Assert.AreEqual("two", users[1].UserName);
                Assert.AreEqual("current", session.Capture().Token);
            }
        }
        /// <summary>401 清除当前缓存，403 保留当前身份。</summary>
        [TestMethod]
        public async Task CurrentUnauthorizedClearsButForbiddenKeepsIdentity()
        {
            foreach (var status in new[] { HttpStatusCode.Forbidden, HttpStatusCode.Unauthorized })
            {
                var store = new MemoryStore(); var session = new SessionService(store); session.TryCommit(0, "current", User("current"));
                var transport = new DeferredHandler(); transport.Response.SetResult(new HttpResponseMessage(status));
                using (var client = new HttpClient(new AuthTokenHandler(session) { InnerHandler = transport }))
                using (await client.GetAsync("https://example.test/data")) { }
                Assert.AreEqual(status == HttpStatusCode.Forbidden ? "current" : null, store.Token);
            }
        }
        /// <summary>损坏或旧版本的提交不能覆盖已登出的会话。</summary>
        [TestMethod]
        public async Task LogoutDuringCandidateValidationRejectsCompletion()
        {
            var store = new MemoryStore(); var session = new SessionService(store); var gateway = new Gateway();
            var task = new AuthService(gateway, session, store).LoginAsync("new", "password", CancellationToken.None);
            session.TryEnd(0); gateway.Me.SetResult(User("new"));
            Assert.IsFalse(await task); Assert.IsNull(store.Token);
        }
        private sealed class AuthTransport : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var login = request.RequestUri.AbsolutePath.EndsWith("/login");
                if (login) Assert.IsNull(request.Headers.Authorization);
                var json = login ? "{\"access_token\":\"candidate\"}" : "{\"UserId\":\"id\",\"UserName\":\"" + request.Headers.Authorization.Parameter + "\"}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json"), RequestMessage = request });
            }
        }
        private sealed class MemoryStore : ITokenStore
        {
            public string Token;
            public string Read() { return Token; }
            public void Write(string token) { Token = token; }
            public void Clear() { Token = null; }
        }
        private sealed class Gateway : IAuthGateway
        {
            public int LoginCount;
            public readonly TaskCompletionSource<MeDto> Me = new TaskCompletionSource<MeDto>();
            public Task<string> LoginAsync(string name, string password) { LoginCount++; return Task.FromResult("new-token"); }
            public Task<MeDto> GetMeAsync(string token) { return Me.Task; }
        }
        private sealed class DeferredHandler : HttpMessageHandler
        {
            public string Token;
            public readonly TaskCompletionSource<HttpResponseMessage> Response = new TaskCompletionSource<HttpResponseMessage>();
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            { Token = request.Headers.Authorization.Parameter; return Response.Task; }
        }
        private sealed class InlineDispatcher : IUiDispatcher
        {
            public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        }
    }
}

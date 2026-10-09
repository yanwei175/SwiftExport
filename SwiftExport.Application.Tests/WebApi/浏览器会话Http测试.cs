using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;
using SwiftExport.WebApi.Authentication;

namespace SwiftExport.Application.Tests.WebApi;

/// <summary>通过一次性 HTTPS 宿主验证浏览器原 JWT 恢复协议，复用生产 JWT/防伪/凭据版本管道，不访问业务库。</summary>
[TestClass]
public sealed class BrowserSessionHttpTests
{
    private FirstWebContractHost _host = null!;
    private const string LoginJson = "{\"userName\":\"contract-user\",\"password\":\"contract-password\"}";

    /// <summary>每个场景独立 Cookie 容器及一次性证书，不改变开发证书和操作系统信任。</summary>
    [TestInitialize]
    public async Task StartAsync() { _host = new(); await _host.StartAsync(browserHttps: true); }

    /// <summary>释放监听、Cookie 客户端和仅存于进程内的证书。</summary>
    [TestCleanup]
    public async Task StopAsync() => await _host.DisposeAsync();

    /// <summary>不带 Web 标记的原登录仍返回裸十二小时 Token DTO，不要求防伪、不设置恢复 Cookie。</summary>
    [TestMethod]
    public async Task WpfLogin_RemainsCompatibleAndDoesNotCreateCookie()
    {
        var before = DateTime.UtcNow;
        using var response = await _host.Client.PostAsync("api/auth/login", Json());
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
        var token = await ReadToken(response);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.IsTrue(jwt.ValidTo >= before.AddHours(12).AddSeconds(-1) && jwt.ValidTo <= DateTime.UtcNow.AddHours(12));
        using var restore = await _host.Client.GetAsync("api/auth/browser-session/restore");
        await Failure(restore, 401);
    }

    /// <summary>匿名防伪 Cookie 覆盖原登录路径且具有严格安全属性，响应不缓存。</summary>
    [TestMethod]
    public async Task Csrf_UsesAnonymousSecureCookieAndNoStore()
    {
        using var response = await _host.Client.GetAsync("api/auth/browser-session/csrf");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        var token = (string?)JObject.Parse(await response.Content.ReadAsStringAsync())["requestToken"];
        Assert.IsFalse(string.IsNullOrWhiteSpace(token));
        AssertSecureCookie(response.Headers.GetValues("Set-Cookie").Single(), BrowserSessionProtocol.CsrfCookie, "/api/auth");
        Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
        Assert.AreEqual(0, _host.AuthenticationCalls);
        Assert.AreEqual(0, _host.CredentialValidationCalls);
    }

    /// <summary>完整 Web 登录、恢复和退出使用同一匿名防伪令牌；恢复不续签，退出不撤销其他 Bearer。</summary>
    [TestMethod]
    public async Task BrowserLoginRestoreLogout_KeepOriginalTokenAndAnonymousCsrfIdentity()
    {
        var csrf = await GetCsrf();
        using var login = await WebLogin(csrf);
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        var original = await ReadToken(login);
        var cookie = login.Headers.GetValues("Set-Cookie").Single(value => value.StartsWith(BrowserSessionProtocol.RestoreCookie + "=", StringComparison.Ordinal));
        AssertSecureCookie(cookie, BrowserSessionProtocol.RestoreCookie, BrowserSessionProtocol.Prefix);
        var expires = SetCookieHeaderValue.Parse(cookie).Expires;
        Assert.AreEqual(new DateTimeOffset(new JwtSecurityTokenHandler().ReadJwtToken(original).ValidTo), expires);
        Assert.IsTrue(login.Headers.CacheControl?.NoStore == true);

        using var restore = await _host.Client.GetAsync("api/auth/browser-session/restore");
        Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
        Assert.AreEqual(original, await ReadToken(restore));
        Assert.IsFalse(restore.Headers.Contains("Set-Cookie"));
        Assert.IsTrue(restore.Headers.CacheControl?.NoStore == true);
        Assert.AreEqual(1, _host.CredentialValidationCalls);

        using var cookieOnlyMe = await _host.Client.GetAsync("api/auth/me");
        await Failure(cookieOnlyMe, 401);
        using var logoutRequest = new HttpRequestMessage(HttpMethod.Post, "api/auth/browser-session/logout");
        logoutRequest.Headers.Add(BrowserSessionProtocol.CsrfHeader, csrf);
        using var logout = await _host.Client.SendAsync(logoutRequest);
        Assert.AreEqual(HttpStatusCode.NoContent, logout.StatusCode);
        Assert.AreEqual("", await logout.Content.ReadAsStringAsync());
        var deleted = logout.Headers.GetValues("Set-Cookie").Single();
        AssertSecureCookie(deleted, BrowserSessionProtocol.RestoreCookie, BrowserSessionProtocol.Prefix);
        Assert.IsTrue(SetCookieHeaderValue.Parse(deleted).Expires < DateTimeOffset.UtcNow);

        using var verify = await _host.Client.GetAsync("api/auth/browser-session/restore");
        await Failure(verify, 401);
        using var bearerMe = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        bearerMe.Headers.Authorization = new AuthenticationHeaderValue("Bearer", original);
        using var stillValid = await _host.Client.SendAsync(bearerMe);
        Assert.AreEqual(HttpStatusCode.OK, stillValid.StatusCode, "删除恢复 Cookie 不撤销原 JWT。");
    }

    /// <summary>Web 登录缺失或错误防伪输入必须在账号认证/签发前拒绝，不能退回 WPF 模式。</summary>
    [TestMethod]
    [DataRow(null, "1")]
    [DataRow("invalid-csrf", "1")]
    [DataRow(null, "unexpected")]
    public async Task BrowserLogin_RejectsMissingCsrfAndInvalidMode(string? csrf, string mode)
    {
        using var response = await WebLogin(csrf, mode);
        await Failure(response, 400);
        Assert.AreEqual(0, _host.AuthenticationCalls);
        Assert.IsFalse(response.Headers.TryGetValues("Set-Cookie", out var cookies)
            && cookies.Any(value => value.StartsWith(BrowserSessionProtocol.RestoreCookie + "=", StringComparison.Ordinal)));
    }

    /// <summary>错误账号凭据不创建恢复 Cookie；防伪通过本身不意味着认证成功。</summary>
    [TestMethod]
    public async Task BrowserLogin_BadCredentialsDoNotCreateRecoveryCookie()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login") { Content = Json("{\"userName\":\"contract-user\",\"password\":\"bad\"}") };
        request.Headers.Add(BrowserSessionProtocol.WebModeHeader, "1");
        request.Headers.Add(BrowserSessionProtocol.CsrfHeader, await GetCsrf());
        using var response = await _host.Client.SendAsync(request);
        await Failure(response, 401);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>协议端点在认证之前拒绝所有 Authorization，避免 Header 验证身份与返回 Cookie 令牌不一致。</summary>
    [TestMethod]
    [DataRow("GET", "api/auth/browser-session/csrf")]
    [DataRow("GET", "api/auth/browser-session/restore")]
    [DataRow("POST", "api/auth/browser-session/logout")]
    [DataRow("POST", "api/auth/login")]
    public async Task BrowserProtocol_RejectsAuthorizationBeforeJwt(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", CreateTestToken(DateTime.UtcNow.AddHours(1)));
        request.Headers.Add("Cookie", BrowserSessionProtocol.RestoreCookie + "=unvalidated-other-token");
        if (path.EndsWith("login", StringComparison.Ordinal))
        {
            request.Headers.Add(BrowserSessionProtocol.WebModeHeader, "1");
            request.Content = Json();
        }
        using var response = await _host.Client.SendAsync(request);
        await Failure(response, 400);
        Assert.AreEqual(0, _host.CredentialValidationCalls);
        Assert.AreEqual(0, _host.AuthenticationCalls);
    }

    /// <summary>仅 Cookie 不能绕过凭据版本变化，即使 JWT 密码学和到期验证仍可通过。</summary>
    [TestMethod]
    public async Task Restore_RejectsChangedCredentialVersion()
    {
        using var login = await WebLogin(await GetCsrf());
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        _host.CredentialValid = false;
        using var response = await _host.Client.GetAsync("api/auth/browser-session/restore");
        await Failure(response, 401);
        Assert.AreEqual(1, _host.CredentialValidationCalls);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>恢复严守原 exp，不能借普通 Bearer 的既有时钟容差延长十二小时免登录窗口。</summary>
    [TestMethod]
    public async Task Restore_RejectsOriginalExpiryWithinBearerClockTolerance()
    {
        var token = CreateTestToken(DateTime.UtcNow.AddSeconds(-2));
        using var ordinary = new HttpRequestMessage(HttpMethod.Get, "api/auth/me");
        ordinary.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var ordinaryResponse = await _host.Client.SendAsync(ordinary);
        Assert.AreEqual(HttpStatusCode.OK, ordinaryResponse.StatusCode, "普通 Bearer 保持现有时钟容差。");
        using var restore = new HttpRequestMessage(HttpMethod.Get, "api/auth/browser-session/restore");
        restore.Headers.Add("Cookie", BrowserSessionProtocol.RestoreCookie + "=" + token);
        using var response = await _host.Client.SendAsync(restore);
        await Failure(response, 401);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>Cookie 恢复使用原密码学验证参数，错误签名、issuer 或 audience 都不能成为本地身份。</summary>
    [TestMethod]
    [DataRow("signature")]
    [DataRow("issuer")]
    [DataRow("audience")]
    public async Task Restore_RejectsInvalidJwtContract(string invalid)
    {
        var token = CreateTestToken(DateTime.UtcNow.AddHours(1), invalid);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/auth/browser-session/restore");
        request.Headers.Add("Cookie", BrowserSessionProtocol.RestoreCookie + "=" + token);
        using var response = await _host.Client.SendAsync(request);
        await Failure(response, 401);
        Assert.AreEqual(0, _host.CredentialValidationCalls);
    }

    /// <summary>退出防伪失败时保留可恢复会话，不返回假成功或执行 Cookie 删除。</summary>
    [TestMethod]
    public async Task Logout_WithoutCsrfDoesNotDeleteRecoveryCookie()
    {
        using var login = await WebLogin(await GetCsrf());
        Assert.AreEqual(HttpStatusCode.OK, login.StatusCode);
        using var logout = await _host.Client.PostAsync("api/auth/browser-session/logout", null);
        await Failure(logout, 400);
        Assert.IsFalse(logout.Headers.Contains("Set-Cookie"));
        using var restore = await _host.Client.GetAsync("api/auth/browser-session/restore");
        Assert.AreEqual(HttpStatusCode.OK, restore.StatusCode);
    }

    /// <summary>相同匿名防伪令牌可以幂等重试退出；第一次退出不改防伪身份或创建服务器撤销状态。</summary>
    [TestMethod]
    public async Task Logout_CanRetryWithSameAnonymousCsrf()
    {
        var csrf = await GetCsrf();
        for (var i = 0; i < 2; i++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/browser-session/logout");
            request.Headers.Add(BrowserSessionProtocol.CsrfHeader, csrf);
            using var response = await _host.Client.SendAsync(request);
            Assert.AreEqual(HttpStatusCode.NoContent, response.StatusCode);
        }
    }

    /// <summary>浏览器协议拒绝不具备 HTTPS 的来源；测试不以 localhost 的浏览器特殊规则替代生产安全边界。</summary>
    [TestMethod]
    public async Task BrowserProtocol_RequiresHttps()
    {
        await using var http = new FirstWebContractHost();
        await http.StartAsync();
        using var response = await http.Client.GetAsync("api/auth/browser-session/csrf");
        await Failure(response, 400);
        Assert.IsFalse(response.Headers.Contains("Set-Cookie"));
    }

    /// <summary>取匿名防伪令牌，Cookie 由本场景客户端容器保存，不读取用户真实浏览器内容。</summary>
    private async Task<string> GetCsrf()
    {
        using var response = await _host.Client.GetAsync("api/auth/browser-session/csrf");
        Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
        return (string)JObject.Parse(await response.Content.ReadAsStringAsync())["requestToken"]!;
    }

    /// <summary>显式选择 Web 模式，使用合成测试凭据和匿名防伪头。</summary>
    private async Task<HttpResponseMessage> WebLogin(string? csrf, string mode = "1")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/auth/login") { Content = Json() };
        request.Headers.Add(BrowserSessionProtocol.WebModeHeader, mode);
        if (csrf is not null) request.Headers.Add(BrowserSessionProtocol.CsrfHeader, csrf);
        return await _host.Client.SendAsync(request);
    }

    /// <summary>仅为边界测试按宿主既有密钥生成可控制到期时间的合成 JWT，不修改生产签发器。</summary>
    private string CreateTestToken(DateTime expires, string? invalid = null)
    {
        var settings = _host.App.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var key = invalid == "signature" ? new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-test-only-signature-key-at-least-32-bytes")) : settings.IssuerSigningKey;
        var token = new JwtSecurityToken(
            invalid == "issuer" ? "different-issuer" : settings.ValidIssuer,
            invalid == "audience" ? "different-audience" : settings.ValidAudience,
            [new Claim(JwtRegisteredClaimNames.Sub, _host.UserId.ToString()), new Claim(AuthTokenClaims.CredentialVersion, "3")],
            expires: expires,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>读取裸 Token 响应；不保存到磁盘或输出到测试日志。</summary>
    private static async Task<string> ReadToken(HttpResponseMessage response) =>
        (string)JObject.Parse(await response.Content.ReadAsStringAsync())["access_token"]!;

    /// <summary>按原 JSON 契约发送合成登录请求，避免客户端序列化掩盖绑定差异。</summary>
    private static StringContent Json(string json = LoginJson) => new(json, Encoding.UTF8, "application/json");

    /// <summary>Cookie 安全选项和作用路径属于协议验收；不打印令牌值。</summary>
    private static void AssertSecureCookie(string cookie, string name, string path)
    {
        var parsed = SetCookieHeaderValue.Parse(cookie);
        Assert.AreEqual(name, parsed.Name.Value);
        Assert.AreEqual(path, parsed.Path.Value);
        Assert.IsTrue(parsed.Secure);
        Assert.IsTrue(parsed.HttpOnly);
        Assert.AreEqual(Microsoft.Net.Http.Headers.SameSiteMode.Strict, parsed.SameSite);
        Assert.IsFalse(parsed.Domain.HasValue);
    }

    /// <summary>检查既有失败包装以及本协议不缓存；不依赖中文消息分析结果。</summary>
    private static async Task Failure(HttpResponseMessage response, int status)
    {
        Assert.AreEqual(status, (int)response.StatusCode);
        var body = JObject.Parse(await response.Content.ReadAsStringAsync());
        Assert.AreEqual(false, (bool?)body["success"]);
        Assert.AreEqual(status == 401 ? "UNAUTHENTICATED" : "VALIDATION", (string?)body.SelectToken("error.code"));
        Assert.AreEqual(JTokenType.Null, body["data"]!.Type);
        Assert.IsTrue(response.Headers.Contains("X-Correlation-ID"));
        // 普通 me 不应用浏览器协议头；其他场景都应防止认证/防伪响应缓存。
        if (response.RequestMessage?.RequestUri?.AbsolutePath.StartsWith("/api/auth/browser-session", StringComparison.Ordinal) == true
            || response.RequestMessage?.Headers.Contains(BrowserSessionProtocol.WebModeHeader) == true)
            Assert.IsTrue(response.Headers.CacheControl?.NoStore == true);
    }
}

using System;
using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Client.VSTO.Api;
using SwiftExport.Client.VSTO.Diagnostics;

namespace SwiftExport.Client.VSTO.Tests
{
    /// <summary>验证 Excel 插件可读取统一错误提示，且不会展示网关正文。</summary>
    [TestClass]
    public sealed class ApiErrorContractTests
    {
        /// <summary>非成功 HTTP 保留服务端已整理的提示。</summary>
        [TestMethod]
        public async Task StructuredFailureUsesSafeMessage()
        {
            var exception = await CreateAsync("{\"success\":false,\"data\":null,\"error\":{\"code\":\"VERSION_CONFLICT\",\"message\":\"请刷新版本\"}}");
            Assert.AreEqual("请刷新版本", new ApiCallRunner(new AddInLog()).Describe(exception));
        }

        /// <summary>损坏正文退回安全提示。</summary>
        [TestMethod]
        public async Task InvalidBodyIsNotShown()
        {
            var exception = await CreateAsync("<html>secret</html>");
            Assert.IsFalse(new ApiCallRunner(new AddInLog()).Describe(exception).Contains("secret"));
        }

        /// <summary>证书信任错误保留分类，任何层级的消息都不进入日志。</summary>
        [TestMethod]
        public void TransportDiagnosticsExcludeSensitiveMessages()
        {
            var inner = new WebException("password=private-token", null, WebExceptionStatus.TrustFailure, null);
            var exception = new HttpRequestException("https://server/?token=private-token", inner);
            var text = AddInLog.FormatException(exception);
            StringAssert.Contains(text, "HttpRequestException");
            StringAssert.Contains(text, "WebStatus=TrustFailure");
            StringAssert.Contains(text, "HResult=0x");
            Assert.IsFalse(text.Contains("private-token"));
            Assert.IsFalse(text.Contains("https://"));
        }

        /// <summary>嵌套 Socket 错误能够区分连接被拒绝与证书问题。</summary>
        [TestMethod]
        public void TransportDiagnosticsIncludeSocketCode()
        {
            var exception = new HttpRequestException("secret", new SocketException((int)SocketError.ConnectionRefused));
            StringAssert.Contains(AddInLog.FormatException(exception), "SocketError=ConnectionRefused");
        }

        /// <summary>限制异常链长度，空异常不产生误导信息。</summary>
        [TestMethod]
        public void TransportDiagnosticsAreBounded()
        {
            Exception exception = new Exception("secret");
            for (var index = 0; index < 20; index++) exception = new Exception("secret", exception);
            var text = AddInLog.FormatException(exception);
            Assert.AreEqual(8, text.Split(new[] { "HResult=" }, StringSplitOptions.None).Length - 1);
            Assert.AreEqual(string.Empty, AddInLog.FormatException(null));
        }

        /// <summary>插件传输显式启用 TLS 1.2，同时保留平台证书验证与宿主全局协议。</summary>
        [TestMethod]
        public void PluginTransportUsesTls12WithoutChangingHostPolicy()
        {
            var original = ServicePointManager.SecurityProtocol;
            using (var transport = ApiClientRegistration.CreateTransport())
            {
                Assert.AreEqual(SslProtocols.Tls12, transport.SslProtocols);
                Assert.IsNull(transport.ServerCertificateCustomValidationCallback);
                Assert.AreEqual(original, ServicePointManager.SecurityProtocol);
            }
        }

        /// <summary>创建带响应内容的真实 Refit 异常。</summary>
        private static Task<ApiException> CreateAsync(string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "https://localhost/test");
            var response = new HttpResponseMessage(HttpStatusCode.Conflict)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            return ApiException.Create(request, HttpMethod.Post, response, new RefitSettings());
        }
    }
}

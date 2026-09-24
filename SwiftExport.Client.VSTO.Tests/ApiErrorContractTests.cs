using System.Net;
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

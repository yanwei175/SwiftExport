using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Refit;
using SwiftExport.Contracts.Interfaces;
using System.Net.Http;

namespace SwiftExport.UI.Services;

//这是一个静态单例，整个插件生命周期只有一份
public static class GlobalApi
{
    public static IProductApi? Client { get; private set; }

    public static void Initialize()
    {
        // 忽略 SSL
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true
        };

        var httpClient = new HttpClient(handler)
        {
            // ⚠️ 记得改成你当前的端口
            BaseAddress = new Uri("https://localhost:7086")
        };

        var refitSettings = new RefitSettings
        {
            ContentSerializer = new NewtonsoftJsonContentSerializer(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                NullValueHandling = NullValueHandling.Ignore
            })
        };

        Client = RestService.For<IProductApi>(httpClient, refitSettings);
    }
}
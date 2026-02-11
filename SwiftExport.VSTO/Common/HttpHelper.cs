using Newtonsoft.Json;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.VSTO.Common
{
    public static class HttpHelper
    {
        private static readonly HttpClient _client = new HttpClient();
        public static async Task<T> GetAsync<T>(string url)
        {
            var json = await _client.GetStringAsync(url); return JsonConvert.DeserializeObject<T>(json);
        }
        public static async Task<T> PostAsync<T>(string url, object body)
        {
            var jsonBody = JsonConvert.SerializeObject(body);
            var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            var response = await _client.PostAsync(url, content);
            var json = await response.Content.ReadAsStringAsync(); return JsonConvert.DeserializeObject<T>(json);
        }
    }
}

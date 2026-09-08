using System;
using System.IO;
using System.Xml.Linq;

namespace SwiftExport.Client.VSTO.Bootstrapper
{
    /// <summary>从插件目录显式加载服务器地址，不读取 Excel.exe.config。</summary>
    public sealed class ApiOptions
    {
        public Uri BaseAddress { get; private set; }
        /// <summary>读取受信 HTTPS 地址；错误配置阻止网络初始化。</summary>
        public static ApiOptions Load(string directory)
        {
            var document = XDocument.Load(Path.Combine(directory, "vsto.config.xml"));
            Uri address;
            if (!Uri.TryCreate((string)document.Root.Element("ApiBaseUrl"), UriKind.Absolute, out address)
                || address.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(address.UserInfo)
                || !string.IsNullOrEmpty(address.Query) || !string.IsNullOrEmpty(address.Fragment))
                throw new InvalidOperationException("请配置有效的 HTTPS API 根地址。");
            return new ApiOptions { BaseAddress = address };
        }
    }
}

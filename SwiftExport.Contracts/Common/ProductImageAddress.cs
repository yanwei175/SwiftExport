using System;
using System.Collections.Generic;
using System.Linq;

namespace SwiftExport.Contracts.Common
{
    /// <summary>统一产品图片基址和安全文件名规则；不涉及物理存储或图片下载。</summary>
    public static class ProductImageAddress
    {
        private static readonly char[] InvalidNameCharacters = { '<', '>', ':', '"', '/', '\\', '|', '?', '*' };
        private static readonly HashSet<string> ReservedNames = CreateReservedNames();

        /// <summary>校验并规范化原图目录地址；拒绝凭据、查询、片段和缩略图目录。</summary>
        public static bool TryNormalizeBaseUrl(string value, out Uri baseUri)
        {
            baseUri = null;
            Uri uri;
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
                string.IsNullOrEmpty(uri.Host) || !string.IsNullOrEmpty(uri.UserInfo) ||
                !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
                uri.AbsolutePath.Split('/').Any(segment => Uri.UnescapeDataString(segment).Equals("Thumbnails", StringComparison.OrdinalIgnoreCase)))
                return false;
            var normalized = uri.AbsoluteUri.TrimEnd('/') + "/";
            if (normalized.Length > 2048) return false;
            baseUri = new Uri(normalized, UriKind.Absolute);
            return true;
        }

        /// <summary>校验单个文件主体，保留内部点号；上传和编码生成使用相同规则。</summary>
        public static bool IsValidFileStem(string stem)
        {
            return !string.IsNullOrEmpty(stem) && stem.IndexOfAny(InvalidNameCharacters) < 0 &&
                !stem.StartsWith(".", StringComparison.Ordinal) && !stem.EndsWith(".", StringComparison.Ordinal) &&
                !stem.EndsWith(" ", StringComparison.Ordinal) && !stem.Any(char.IsControl) &&
                !ReservedNames.Contains(stem.Split('.')[0]);
        }

        /// <summary>使用完整产品编码生成 JPG 文件名，不把编码内的点号当成扩展名。</summary>
        public static bool TryGetProductFileName(string productCode, out string fileName)
        {
            var stem = productCode == null ? null : productCode.Trim();
            fileName = IsValidFileStem(stem) ? stem + ".jpg" : null;
            return fileName != null;
        }

        /// <summary>将安全文件名作为单个 URI 段拼接，可选择服务器的固定缩略图目录。</summary>
        public static string BuildUrl(Uri baseUri, string fileName, bool thumbnail = false)
            => new Uri(baseUri, (thumbnail ? "Thumbnails/" : string.Empty) + Uri.EscapeDataString(fileName)).AbsoluteUri;

        /// <summary>阻止 Windows 保留设备名，保持本地存储与公开服务可访问性一致。</summary>
        private static HashSet<string> CreateReservedNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "CON", "PRN", "AUX", "NUL", "CLOCK$", "CONIN$", "CONOUT$" };
            for (var i = 1; i <= 9; i++) { names.Add("COM" + i); names.Add("LPT" + i); }
            foreach (var suffix in new[] { '¹', '²', '³' }) { names.Add("COM" + suffix); names.Add("LPT" + suffix); }
            return names;
        }
    }
}

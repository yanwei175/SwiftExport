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

        /// <summary>规范重建基址，接受网络原图目录或 Windows 绝对目录；不检查客户端目录是否可达。</summary>
        public static bool TryNormalizeBaseAddress(string value, out string baseAddress)
        {
            baseAddress = null;
            Uri baseUri;
            if (TryNormalizeBaseUrl(value, out baseUri))
            {
                baseAddress = baseUri.AbsoluteUri;
                return true;
            }
            if (string.IsNullOrWhiteSpace(value)) return false;
            var path = value.Trim().Replace('/', '\\');
            if (path.Length > 2048 || path.Any(char.IsControl)) return false;
            var driveRoot = path.Length >= 3 && ((path[0] >= 'A' && path[0] <= 'Z') || (path[0] >= 'a' && path[0] <= 'z'))
                && path[1] == ':' && path[2] == '\\';
            var uncRoot = path.StartsWith("\\\\", StringComparison.Ordinal);
            if (!driveRoot && !uncRoot) return false;
            var segments = path.Substring(driveRoot ? 3 : 2).Split(new[] { '\\' }, StringSplitOptions.RemoveEmptyEntries);
            if (uncRoot && segments.Length < 2) return false;
            // 按 Windows 目录规则校验文本，服务端运行在其他系统时也不依赖其 Path 语义。
            if (segments.Any(segment => segment == "." || segment == ".." || segment.IndexOfAny(InvalidNameCharacters) >= 0
                || segment.EndsWith(".", StringComparison.Ordinal) || segment.EndsWith(" ", StringComparison.Ordinal)
                || ReservedNames.Contains(segment.Split('.')[0]))) return false;
            baseAddress = (driveRoot ? path.Substring(0, 3) : "\\\\") + string.Join("\\", segments);
            baseAddress = baseAddress.TrimEnd('\\') + "\\";
            return baseAddress.Length <= 2048;
        }

        /// <summary>按当前重建基址生成完整产品图片引用；本地文件名保留原字符，网络文件名按 URI 段转义。</summary>
        public static bool TryBuildProductImageReference(string baseAddress, string productCode, out string reference)
        {
            reference = null;
            string normalized;
            string fileName;
            if (!TryNormalizeBaseAddress(baseAddress, out normalized) || !TryGetProductFileName(productCode, out fileName)) return false;
            Uri baseUri;
            reference = TryNormalizeBaseUrl(normalized, out baseUri) ? BuildUrl(baseUri, fileName) : normalized + fileName;
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

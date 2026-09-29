using System;
using System.Text;

namespace SwiftExport.Contracts.Common
{
    /// <summary>统一服务端保存与客户端匹配 Excel 表头的规范化规则。</summary>
    public static class ExcelHeaderNormalizer
    {
        /// <summary>执行兼容字符归一、空白折叠及大小写归一；空输入返回空字符串。</summary>
        public static string Normalize(string header)
        {
            if (string.IsNullOrWhiteSpace(header)) return string.Empty;
            var source = header.Normalize(NormalizationForm.FormKC).Trim();
            var result = new StringBuilder(source.Length);
            var pendingSpace = false;
            foreach (var character in source)
            {
                if (char.IsWhiteSpace(character))
                {
                    pendingSpace = result.Length > 0;
                    continue;
                }
                if (pendingSpace) result.Append(' ');
                pendingSpace = false;
                result.Append(char.ToUpperInvariant(character));
            }
            return result.ToString();
        }
    }
}

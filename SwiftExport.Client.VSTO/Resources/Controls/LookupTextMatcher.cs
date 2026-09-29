using System;
using System.Collections.Generic;
using System.Linq;
namespace SwiftExport.Client.VSTO.Resources.Controls
{

    /// <summary>Lookup 本地搜索的文本匹配规则。</summary>
    internal static class LookupTextMatcher
    {
        /// <summary>
        /// 按输入顺序匹配各个非空片段；片段之间的空格表示任意长度的字符序列。
        /// </summary>
        public static bool IsMatch(string candidate, string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return true;
            }

            if (string.IsNullOrEmpty(candidate))
            {
                return false;
            }

            var segments = query.Split(
                new[] { ' ' },
                StringSplitOptions.RemoveEmptyEntries)
                .Select(segment => segment.Trim()).Where(segment => segment.Length > 0).ToArray();
            if (segments.Length == 0)
            {
                return true;
            }

            var searchStart = 0;
            foreach (var segment in segments)
            {
                var matchIndex = candidate.IndexOf(segment, searchStart, StringComparison.OrdinalIgnoreCase);
                if (matchIndex < 0)
                {
                    return false;
                }

                searchStart = matchIndex + segment.Length;
            }

            return true;
        }
    }

}

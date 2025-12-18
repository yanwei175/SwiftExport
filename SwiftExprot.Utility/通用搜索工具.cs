using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SwiftExprot.Utility
{
    public static class 通用搜索工具
    {
        /// <summary>
        /// 空格分隔的模糊匹配
        /// </summary>
        /// <param name="source">被比对的文本</param>
        /// <param name="keywords">关键字字符串，空格分隔</param>
        /// <returns>是否匹配</returns>
        public static bool 空格模糊匹配(string source, string keywords)
        {
            if (string.IsNullOrWhiteSpace(keywords))                return true; // 没有关键字时默认匹配

            if (string.IsNullOrEmpty(source))                return false;

            // 拆分关键字（按空格）
            var parts = keywords.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            // 每个关键字只要在 source 中出现即可（AND逻辑：必须全部包含）
            return parts.All(part =>
                source.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0);
        }
    }
}

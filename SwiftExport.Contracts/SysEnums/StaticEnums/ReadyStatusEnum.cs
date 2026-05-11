using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public class ReadyStatusEnum
    {    /// <summary>
         /// 采购单状态枚举
         /// </summary>
            public const int 未跟踪 = 0;
            public const int 部分完工 = 1;
            public const int 全部完工 = 2;
            public const int 已关闭 = 9;

            public static readonly IReadOnlyList<KeyValuePair<int, string>> Items = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(未跟踪, "未跟踪"),
                new KeyValuePair<int, string>(部分完工, "部分完工"),
                new KeyValuePair<int, string>(全部完工, "全部完工"),
                new KeyValuePair<int, string>(已关闭, "已关闭")
            };
        
    }
}

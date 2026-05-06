using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 销售单状态枚举
    /// </summary>
    public static class SalesOrderStatusEnum
    {
        public const int 草稿 = 0;
        public const int 已确认 = 1;
        public const int 部分发货 = 2;
        public const int 全部发货 = 3;
        public const int 已关闭 = 9;

        public static IReadOnlyList<KeyValuePair<int, string>> Items = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(草稿, "草稿"),
            new KeyValuePair<int, string>(已确认, "已确认"),
            new KeyValuePair<int, string>(部分发货, "部分发货"),
            new KeyValuePair<int, string>(全部发货, "全部发货"),
            new KeyValuePair<int, string>(已关闭, "已关闭")
        };
    }
}

using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 装箱单状态枚举
    /// </summary>
    public static class PackingListStatusEnum
    {
        public const int 草稿 = 0;
        public const int 已发货 = 1;
        public const int 已关闭 = 9;
        public static List<KeyValuePair<int, string>> PackingListStatusDicList => new List<KeyValuePair<int, string>>()
        {
            new KeyValuePair<int, string>(0, "草稿"),
            new KeyValuePair<int, string>(1, "已发货"),
            new KeyValuePair<int, string>(9, "已关闭")
        };

    }

}

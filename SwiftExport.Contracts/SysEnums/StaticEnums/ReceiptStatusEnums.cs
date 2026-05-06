using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 收货单枚举
    /// </summary>
    public static class ReceiptStatusEnum
    {
        public const int 草稿 = 0;
        public const int 已收货 = 1;
        public const int 已关闭 = 9;
        public static List<KeyValuePair<int, string>> ReceiptStatusDicList => new List<KeyValuePair<int, string>>()
        {
            new KeyValuePair<int, string>(0, "草稿"),
            new KeyValuePair<int, string>(1, "已收货"),
            new KeyValuePair<int, string>(9, "已关闭")
        };

    }

}

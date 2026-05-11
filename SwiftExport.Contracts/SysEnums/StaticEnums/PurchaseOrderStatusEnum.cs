using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 采购单状态枚举
    /// </summary>
    public static class PurchaseOrderStatusEnum
    {
        public const int 草稿 = 0;
        public const int 已下单 = 1;
        public const int 部分完成 = 2;
        public const int 全部完成 = 3;
        public const int 已关闭 = 9;

        /// <summary>
        /// value(int) - name(string) 键值对列表，供前端下拉框等使用。
        /// </summary>
        public static readonly IReadOnlyList<KeyValuePair<int, string>> Items = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(草稿, "草稿"),
                new KeyValuePair<int, string>(已下单, "已下单"),
                new KeyValuePair<int, string>(部分完成, "部分完成"),
                new KeyValuePair<int, string>(全部完成, "全部完成"),
                new KeyValuePair<int, string>(已关闭, "已关闭")
            };
    }

}

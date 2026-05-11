using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 毛需求状态枚举,草稿,已确认,已分解,采购中,部分到货,已到货,已完成,已关闭
    /// </summary>
    public static class IndependentDemandStatusEnum
    {
        public const int 草稿 = 0;
        public const int 已确认 = 1;//已分解成净需求
        public const int 采购中 = 4;
        public const int 部分到货 = 5;
        public const int 已到货 = 6;
        public const int 已完成 = 8;
        public const int 已关闭 = 9;

        public static readonly IReadOnlyList<KeyValuePair<int, string>> Items = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(草稿, "草稿"),
            new KeyValuePair<int, string>(已确认, "已确认"),
            new KeyValuePair<int, string>(采购中, "采购中"),
            new KeyValuePair<int, string>(部分到货, "部分到货"),
            new KeyValuePair<int, string>(已到货, "已到货"),
            new KeyValuePair<int, string>(已完成, "已完成"),
            new KeyValuePair<int, string>(已关闭, "已关闭")
        };
    }

}

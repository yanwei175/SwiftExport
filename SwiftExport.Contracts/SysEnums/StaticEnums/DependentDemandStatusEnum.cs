using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{

    /// <summary>
    /// 净需求状态枚举
    /// </summary>
    public static class DependentDemandStatusEnum
    {
        public const int 草稿 = 0;
        public const int 已转为正式单据 = 3;
        public const int 已关闭 = 9;

        public static readonly IReadOnlyList<KeyValuePair<int, string>> Items = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(草稿, "草稿"),
            new KeyValuePair<int, string>(已转为正式单据, "已转为正式单据"),
            new KeyValuePair<int, string>(已关闭, "已关闭")
        };
    }

}

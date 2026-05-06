using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{

    /// <summary>
    /// 审批状态枚举
    /// </summary>
    public static class ApprovalStatusEnum
    {
        public const int 草稿 = 0;
        public const int 待审批 = 1;
        public const int 已批准 = 2;
        public const int 已驳回 = 3;
        public const int 已取消 = 9;

        public static readonly IReadOnlyList<KeyValuePair<int, string>> Items = new List<KeyValuePair<int, string>>
        {
            new KeyValuePair<int, string>(草稿, "草稿"),
            new KeyValuePair<int, string>(待审批, "待审批"),
            new KeyValuePair<int, string>(已批准, "已批准"),
            new KeyValuePair<int, string>(已驳回, "已驳回"),
            new KeyValuePair<int, string>(已取消, "已取消")
         };
    }

}

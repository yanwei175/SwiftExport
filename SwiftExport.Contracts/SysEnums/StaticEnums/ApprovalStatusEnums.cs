using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// "草稿","待审批","已批准","已驳回","已取消"
    /// </summary>
    public static class ApprovalStatusEnums
    {
        public const string 草稿 = "草稿";
        public const string 待审批 = "待审批";
        public const string 已批准 = "已批准";
        public const string 已驳回 = "已驳回";
        public const string 已取消 = "已取消";
        public static List<string> ApprovalStatus => new List<string>
        {
            "草稿","待审批","已批准","已驳回","已取消"
        };

        public static List<KeyValuePair<string, string>> All()
        {
            return new List<KeyValuePair<string, string>>()
            {
                new KeyValuePair<string, string>("草稿", "草稿"),
                new KeyValuePair<string, string>("待审批", "待审批"),
                new KeyValuePair<string, string>("已批准", "已批准"),
                new KeyValuePair<string, string>("已驳回", "已驳回"),
                new KeyValuePair<string, string>("已取消", "已取消")
            };
        }

    }
}
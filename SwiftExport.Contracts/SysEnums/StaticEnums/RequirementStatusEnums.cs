using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public class RequirementStatusEnums
    {
        public const string 草稿 = "草稿";
        public const string 待处理 = "待处理";
        public const string 采购中 = "采购中";
        public const string 部分到货 = "部分到货";
        public const string 已到货 = "已到货";
        public const string 已完成 = "已完成";

        public static List<string> StatusFlags => new List<string>
        {
            "草稿","待处理","采购中","部分到货","已到货","已完成"
        };
    }
}

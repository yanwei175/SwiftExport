using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public class MaterialTypeEnums
    {
        public const string 成品 = "成品";
        public const string 零件 = "零件";
        public const string 虚拟件 = "虚拟件";
        public const string 客供件 = "客供件";

        public static List<string> MaterialTypes => new List<string>
        {
            "成品","零件","虚拟件","客供件"
        };

        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("成品", "成品"),
            new KeyValuePair<string, string>("零件", "零件"),
            new KeyValuePair<string, string>("虚拟件", "虚拟件"),
            new KeyValuePair<string, string>("客供件", "客供件")
        };
    }
}

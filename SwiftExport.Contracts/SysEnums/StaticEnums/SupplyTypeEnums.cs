using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public static class SupplyTypeEnums
    {
        public const string 按BOM拆分 = "按BOM拆分";
        public const string 直接采购 = "直接采购";

        public static List<string> SupplyTypes => new List<string>
        {
            "按BOM拆分","直接采购"
        };
 
        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("按BOM拆分", "按BOM拆分"),
            new KeyValuePair<string, string>("直接采购", "直接采购")
        };
    }
}

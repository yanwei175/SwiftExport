using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public static class SupplyType
    {
        public const string 按BOM拆分 = "按BOM拆分";
        public const string 直接采购 = "直接采购";
        public const string 含客供的BOM = "含客供的BOM";
        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("按BOM拆分", "按BOM拆分"),
            new KeyValuePair<string, string>("直接采购", "直接采购"),
            new KeyValuePair<string, string>("含客供的BOM", "含客供的BOM")
        };
    }
}

using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    //供货类型枚举,新语义：生产供给、采购供给、外协供给
    public static class SupplyTypeEnums
    {
        /// <summary>
        /// 生产供给（新语义）。
        /// </summary>
        public const string 生产 = "生产";

        /// <summary>
        /// 采购供给（新语义）。
        /// </summary>
        public const string 采购 = "采购";

        /// <summary>
        /// 外协供给（新语义）。
        /// </summary>
        public const string 外协 = "外协";

        public static List<string> SupplyTypes => new List<string>
        {
            生产,采购,外协
        };

        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>(生产, 生产),
            new KeyValuePair<string, string>(采购, 采购),
            new KeyValuePair<string, string>(外协, 外协)
        };
    }
}

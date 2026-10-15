using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 物料类型枚举
    /// </summary>
    public class MaterialTypesEnum
    {
        public const string 成品 = "成品";
        public const string 零件 = "零件";
        public const string 虚拟件 = "虚拟件";
        public const string 客供件 = "客供件";
        /// <summary>作为普通物料维护、采购和参与 BOM 消耗的包装材料。</summary>
        public const string 包材 = "包材";

        /// <summary>物料维护与筛选共用的类型候选，保留既有类型及其顺序。</summary>
        public static List<string> MaterialTypes => new List<string>
        {
            成品, 零件, 虚拟件, 客供件, 包材
        };

        /// <summary>物料类型的稳定存储值与显示值，供键值候选消费。</summary>
        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("成品", "成品"),
            new KeyValuePair<string, string>("零件", "零件"),
            new KeyValuePair<string, string>("虚拟件", "虚拟件"),
            new KeyValuePair<string, string>("客供件", "客供件"),
            new KeyValuePair<string, string>(包材, 包材)
        };
    }
}

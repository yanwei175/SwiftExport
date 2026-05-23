using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>库存流水类型</summary>
    public static class BizTypesEnum
    {
        public const string 采购入库 = "采购入库";
        public const string 销售出库 = "销售出库";
        public const string 库存调整 = "库存调整";
        public const string 采购退货 = "采购退货";

        public static List<string> BizTypes => new List<string>
        {
            "采购入库","销售出库","库存调整","采购退货"
        };

        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("采购入库", "采购入库"),
            new KeyValuePair<string, string>("销售出库", "销售出库"),
            new KeyValuePair<string, string>("库存调整", "库存调整"),
            new KeyValuePair<string, string>("采购退货", "采购退货")
        };
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    /// <summary>
    /// 单据类型枚举
    /// </summary>
    public class OrderTypesEnum
    {
        public const string 销售订单 = "销售订单";
        public const string 采购订单 = "采购订单";
        public const string 计划订单 = "计划订单";

        public static List<string> OrderTypes => new List<string>
        {
            "销售订单","采购订单","计划订单"
        };

        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("销售订单", "销售订单"),
            new KeyValuePair<string, string>("采购订单", "采购订单"),
            new KeyValuePair<string, string>("计划订单", "计划订单")
        };
    }
}

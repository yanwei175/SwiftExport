using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public static class SalesOrderStatusEnums
    {
        public const string 未发货 = "未发货";
        public const string 部分发货 = "部分发货";
        public const string 全部发完 = "全部发完";
        public const string 收到货物 = "收到货物";
        public const string 完成 = "完成";
        public static List<string> SalesOrderStatus => new List<string>
        {
            "未发货","部分发货","全部发完","收到货物","完成"
        };
        public static List<KeyValuePair<string, string>> All => new List<KeyValuePair<string, string>>()
        {
            new KeyValuePair<string, string>("未发货", "未发货"),
            new KeyValuePair<string, string>("部分发货", "部分发货"),
            new KeyValuePair<string, string>("全部发完", "全部发完"),
            new KeyValuePair<string, string>("收到货物", "收到货物"),
            new KeyValuePair<string, string>("完成", "完成")
        };
    }
    }

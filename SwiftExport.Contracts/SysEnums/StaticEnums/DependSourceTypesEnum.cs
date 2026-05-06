using System;
using System.Collections.Generic;
using System.Text;

namespace SwiftExport.Contracts.SysEnums.StaticEnums
{
    public static class DependSourceTypesEnum
    {
        public const string 销售订单 = "销售订单";
        public const string 补单 = "补单";
        public const string 样品单 = "样品单";
        public const string 需求预测 = "需求预测";
        public const string 备货单 = "备货单";

        public static readonly IReadOnlyList<string> SourceTypes = new List<string>
        {
            销售订单,
            补单,
            样品单,
            需求预测,
            备货单
        };
        public static readonly IReadOnlyList<KeyValuePair<string, string>> Items = new List<KeyValuePair<string, string>>
        {
            new KeyValuePair<string, string>(销售订单, "销售订单"),
            new KeyValuePair<string, string>(补单, "补单"),
            new KeyValuePair<string, string>(样品单, "样品单"),
            new KeyValuePair<string, string>(需求预测, "需求预测"),
            new KeyValuePair<string, string>(备货单, "备货单")
        };
    }
}

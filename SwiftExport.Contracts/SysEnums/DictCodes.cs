using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums
{
    public static class DictCodes
    {
        // 首批由通用字典管理维护的系统字典。
        public const string CustomerType = "CustomerType";
        public const string Incoterm = "Incoterm";
        public const string PaymentMethod = "PaymentMethod";

        // 以下常量为旧调用方保留，不进入通用字典管理。
        public const string Currency = "Currency"; // 使用货币及汇率专用主数据。
        public const string ProductColor = "ProductColor"; // 使用产品颜色专用主数据。
        public const string ProductCategory = "ProductCategory"; // 使用产品分类专用主数据。
        public const string ProductType = "ProductType"; // 当前使用固定物料类型枚举。
        public const string IncotermSortRemark = "IncotermSortRemark"; // 业务自由文本。
        public const string IncotermLocation = "IncotermLocation"; // 业务自由文本。

        public static List<string> DictCodeList => new List<string>
        {
            CustomerType,
            Incoterm,
            PaymentMethod
        };
    }
}

using System.Collections.Generic;

namespace SwiftExport.Contracts.SysEnums
{
    /// <summary>
    /// 定义通用系统字典的稳定编码。具体字典值由 SysDictType/SysDictData 维护，
    /// 本类型不重复声明运行时可配置的业务值。
    /// </summary>
    public static class DictCodes
    {
        // 首批由通用字典管理维护的系统字典。
        public const string CustomerType = "CustomerType";
        public const string Incoterm = "Incoterm";
        public const string PaymentMethod = "PaymentMethod";
        /// <summary>运输方式字典编码；具体可选值由数据库字典维护。</summary>
        public const string ShippedBy = "ShippedBy";
        public const string Unit = "Unit";

        // 以下业务字段不属于通用字典值，只保留仍被运行时查询使用的稳定编码。
        public const string ProductType = "ProductType"; // 当前使用固定物料类型枚举。
        public const string IncotermSortRemark = "IncotermSortRemark"; // 业务自由文本。
        public const string IncotermLocation = "IncotermLocation"; // 业务自由文本。

        public static List<string> DictCodeList => new List<string>
        {
            CustomerType,
            Incoterm,
            PaymentMethod,
            ShippedBy,
            Unit
        };
    }
}

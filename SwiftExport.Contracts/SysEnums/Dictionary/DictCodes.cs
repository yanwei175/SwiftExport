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
        /// <summary>旧售后品质字典的迁移识别码；当前分类由独立品质分类树维护。</summary>
        public const string AfterSalesQualityCategory = "AfterSalesQualityCategory";
        /// <summary>流量测试功能字典编码；具体功能由字典维护。</summary>
        public const string FlowTestFunction = "FlowTestFunction";
        /// <summary>流量测试限流器规格字典编码；包含明确的不限流选项。</summary>
        public const string FlowLimiterSpecification = "FlowLimiterSpecification";

        /// <summary>产品属性模板的内置类型编码；模板明细由用户在字典中维护，不预置具体模板。</summary>
        public const string ProductAttributeProfile = "ProductAttributeProfile";

        /// <summary>产品系列的内置类型编码；系列明细由用户维护，不预置具体系列。</summary>
        public const string ProductSeries = "ProductSeries";

        /// <summary>条款备注建议的内置类型编码；业务字段保存自由文本，不要求命中字典明细。</summary>
        public const string IncotermSortRemark = "IncotermSortRemark";
        /// <summary>条款地点建议的内置类型编码；业务字段保存自由文本，不要求命中字典明细。</summary>
        public const string IncotermLocation = "IncotermLocation";

        /// <summary>当前支持的系统字典类型编码，包含可由用户维护明细的建议类型。</summary>
        public static List<string> DictCodeList => new List<string>
        {
            CustomerType,
            Incoterm,
            PaymentMethod,
            ShippedBy,
            Unit,
            FlowTestFunction,
            FlowLimiterSpecification,
            IncotermLocation,
            IncotermSortRemark,
            ProductAttributeProfile,
            ProductSeries
        };
    }
}

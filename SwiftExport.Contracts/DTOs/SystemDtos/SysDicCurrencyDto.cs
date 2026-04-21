using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>
    /// 货币主数据 DTO
    /// 前后端数据传输载体
    /// </summary>
    public class SysDicCurrencyDto : BaseDto
    {
        /// <summary>货币代码，如 USD / CNY / EUR</summary>
        public string CurrencyCode { get; set; }

        /// <summary>货币英文名称</summary>
        public string CurrencyNameEn { get; set; }

        /// <summary>货币中文名称</summary>
        public string CurrencyNameZh { get; set; }

        /// <summary>货币符号，如 $、¥、€</summary>
        public string CurrencySymbol { get; set; }

        public int Precision { get; set; } = 2;

        public decimal RateToBase { get; set; }

        /// <summary>是否本位币（全局唯一）</summary>
        public bool IsBase { get; set; }

        /// <summary>是否系统内置（内置不允许删除）</summary>
        public bool IsSystem { get; set; }

        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; }

        /// <summary>备注</summary>
        public string Remark { get; set; }
    }
}

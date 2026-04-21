using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Systems
{
    /// <summary>
    /// 货币汇率字典实体
    /// 对应数据库表 SysDicCurrencyExchange
    /// </summary>
    public class SysDicCurrencyExchange : BaseEntity
    {
        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>源货币代码（如 USD）</summary>
        public string? FromCurrencyCode { get; set; }

        /// <summary>目标货币代码（如 CNY）</summary>
        public string? ToCurrencyCode { get; set; }

        /// <summary>汇率精度（建议 0~6）</summary>
        public int Precision { get; set; } = 6;

        /// <summary>汇率值</summary>
        public decimal? Rate { get; set; }

        /// <summary>备注</summary>
        public string? Remark { get; set; }
    }
}

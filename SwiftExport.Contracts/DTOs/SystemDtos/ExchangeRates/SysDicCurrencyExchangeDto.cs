using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>
    /// 货币汇率字典 DTO
    /// 前后端数据传输载体
    /// </summary>
    public class SysDicCurrencyExchangeDto : BaseDto
    {
        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; }

        /// <summary>源货币代码（如 USD）</summary>
        public string FromCurrencyCode { get; set; }

        /// <summary>目标货币代码（如 CNY）</summary>
        public string ToCurrencyCode { get; set; }

        /// <summary>汇率精度（建议 0~6）</summary>
        public int Precision { get; set; }

        /// <summary>汇率值</summary>
        public decimal? Rate { get; set; }

        /// <summary>备注</summary>
        public string Remark { get; set; }
    }
}

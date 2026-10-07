using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems.ExchangeRates;

/// <summary>币种汇率实体，用于维护两个币种之间的换算规则。</summary>
[Table("sys_dic_currency_exchange")]
public partial class SysDicCurrencyExchange : BaseEntity
{

    /// <summary>启用标记，用于控制该记录的业务适用范围。</summary>
    public bool IsEnabled { get; set; } = true;


    /// <summary>该实体保存的源币种编码。</summary>
    public string? FromCurrencyCode { get; set; }


    /// <summary>该实体保存的目标币种编码。</summary>
    public string? ToCurrencyCode { get; set; }


    /// <summary>该实体保存的汇率小数精度。</summary>
    public int Precision { get; set; } = 6;


    /// <summary>该实体保存的币种换算汇率。</summary>
    public decimal? Rate { get; set; }

    /// <summary>该实体保存的业务备注。</summary>
    public string? Remark { get; set; }
}

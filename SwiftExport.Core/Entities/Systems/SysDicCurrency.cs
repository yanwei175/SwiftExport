using SwiftExport.Core.Common;
using System.ComponentModel.DataAnnotations.Schema;

namespace SwiftExport.Core.Entities.Systems;

/// <summary>
/// 货币主数据实体
/// 对应数据库表 SysDicCurrency
/// </summary>
[Table("SysDicCurrency")]
public class SysDicCurrency : BaseEntity
{
    /// <summary>货币代码，如 USD / CNY / EUR</summary>
    public string? CurrencyCode { get; set; }

    /// <summary>货币英文名称</summary>
    public string? CurrencyNameEn { get; set; }

    /// <summary>货币中文名称</summary>
    public string? CurrencyNameZh { get; set; }

    /// <summary>货币符号，如 $、¥、€</summary>
    public string? CurrencySymbol { get; set; }
    /// <summary>精确度，如 2 表示保留两位小数</summary>
    public int Precision { get; set; } = 2;
    /// <summary>相对于本位币的汇率</summary>
    public decimal RateToBase { get; set; }

    /// <summary>是否本位币（系统中只能有一个）</summary>
    public bool IsBase { get; set; } = false;
    /// <summary>是否系统内置货币（内置货币不允许删除）</summary>
    public bool IsSystem { get; set; } = true;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>备注</summary>
    public string? Remark { get; set; }
}

using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;

namespace SwiftExport.Core.Entities.QualityControl.FlowManagement;

/// <summary>保存本测试组中一个功能的限流器配置和固定压力点实测结果；两套压力列使用各自固定单位。</summary>
[Table("flow_line")]
[DbIndex("UX_FlowLine_Header_Function", nameof(HeaderId), nameof(FunctionId), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbCheck("CK_FlowLine_SortNo", "\"sort_no\" >= 0")]
[DbCheck("CK_FlowLine_Measurements", "(\"flow_1_bar_l_min\" IS NULL OR \"flow_1_bar_l_min\" >= 0) AND (\"flow_2_bar_l_min\" IS NULL OR \"flow_2_bar_l_min\" >= 0) AND (\"flow_3_bar_l_min\" IS NULL OR \"flow_3_bar_l_min\" >= 0) AND (\"flow_4_bar_l_min\" IS NULL OR \"flow_4_bar_l_min\" >= 0) AND (\"flow_5_bar_l_min\" IS NULL OR \"flow_5_bar_l_min\" >= 0) AND (\"flow_6_bar_l_min\" IS NULL OR \"flow_6_bar_l_min\" >= 0) AND (\"flow_7_bar_l_min\" IS NULL OR \"flow_7_bar_l_min\" >= 0) AND (\"flow_8_bar_l_min\" IS NULL OR \"flow_8_bar_l_min\" >= 0) AND (\"flow_20_psi_gpm\" IS NULL OR \"flow_20_psi_gpm\" >= 0) AND (\"flow_40_psi_gpm\" IS NULL OR \"flow_40_psi_gpm\" >= 0) AND (\"flow_60_psi_gpm\" IS NULL OR \"flow_60_psi_gpm\" >= 0) AND (\"flow_80_psi_gpm\" IS NULL OR \"flow_80_psi_gpm\" >= 0) AND (\"flow_100_psi_gpm\" IS NULL OR \"flow_100_psi_gpm\" >= 0)")]
[DbCheck("CK_FlowLine_HasMeasurement", "COALESCE(\"flow_1_bar_l_min\", \"flow_2_bar_l_min\", \"flow_3_bar_l_min\", \"flow_4_bar_l_min\", \"flow_5_bar_l_min\", \"flow_6_bar_l_min\", \"flow_7_bar_l_min\", \"flow_8_bar_l_min\", \"flow_20_psi_gpm\", \"flow_40_psi_gpm\", \"flow_60_psi_gpm\", \"flow_80_psi_gpm\", \"flow_100_psi_gpm\") IS NOT NULL")]
[LogicalReference(nameof(HeaderId), typeof(FlowHeader), IsRequired = true)]
[LogicalReference(nameof(FunctionId), typeof(SysDictData), IsRequired = true)]
[LogicalReference(nameof(LimiterSpecificationId), typeof(SysDictData), IsRequired = true)]
public sealed class FlowLine : BaseEntity
{
    /// <summary>所属流量测试组身份。</summary>
    public Guid HeaderId { get; set; }
    /// <summary>流量测试功能字典项身份。</summary>
    public Guid FunctionId { get; set; }
    /// <summary>采用该功能字典项时的显示名称，普通编辑不追随字典改名。</summary>
    public string FunctionNameSnapshot { get; set; } = string.Empty;
    /// <summary>本功能使用的限流器规格字典项身份，“不限流”也必须明确选择。</summary>
    public Guid LimiterSpecificationId { get; set; }
    /// <summary>采用该限流器规格时的显示名称，不作为产品实测流量。</summary>
    public string LimiterSpecificationNameSnapshot { get; set; } = string.Empty;
    /// <summary>1 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_1_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow1BarLMin { get; set; }
    /// <summary>2 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_2_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow2BarLMin { get; set; }
    /// <summary>3 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_3_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow3BarLMin { get; set; }
    /// <summary>4 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_4_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow4BarLMin { get; set; }
    /// <summary>5 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_5_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow5BarLMin { get; set; }
    /// <summary>6 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_6_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow6BarLMin { get; set; }
    /// <summary>7 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_7_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow7BarLMin { get; set; }
    /// <summary>8 bar 压力下的实测流量，单位 L/min；空表示未测。</summary>
    [Column("flow_8_bar_l_min")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow8BarLMin { get; set; }
    /// <summary>20 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
    [Column("flow_20_psi_gpm")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow20PsiGpm { get; set; }
    /// <summary>40 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
    [Column("flow_40_psi_gpm")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow40PsiGpm { get; set; }
    /// <summary>60 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
    [Column("flow_60_psi_gpm")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow60PsiGpm { get; set; }
    /// <summary>80 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
    [Column("flow_80_psi_gpm")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow80PsiGpm { get; set; }
    /// <summary>100 PSI 压力下的实测流量，单位美国加仑/分钟；空表示未测。</summary>
    [Column("flow_100_psi_gpm")]
    [DbColumn(StoreType = "numeric(18,4)")]
    public decimal? Flow100PsiGpm { get; set; }
    /// <summary>非负显示排序号。</summary>
    public int SortNo { get; set; }
    /// <summary>具体限流器型号、特殊配件或测量备注。</summary>
    public string? Remark { get; set; }
}


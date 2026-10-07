using System.ComponentModel.DataAnnotations.Schema;
using SwiftExport.Core.Common;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Core.Entities.Systems.ExcelReadProfiles;

/// <summary>保存全员共享的一份 Excel 单据读取配置及明细结束规则；不保存工作簿数据。</summary>
[Table("excel_read_profile")]
[DbIndex("UX_ExcelReadProfile_Group_Name", nameof(GroupCode), nameof(ConfigName), IsUnique = true, Filter = "\"is_deleted\" = FALSE")]
[DbCheck("CK_ExcelReadProfile_EndMode", "\"end_mode\" IN ( 0 , 1 , 2 )")]
public sealed class ExcelReadProfile : BaseEntity
{
    /// <summary>稳定单据分组，与客户端注册的导入模型分组对应，创建后不可修改。</summary>
    [DbColumn(StoreType = "varchar(40)")]
    public required string GroupCode { get; set; }

    /// <summary>同一单据分组内唯一的配置名称。</summary>
    [DbColumn(StoreType = "varchar(100)")]
    public required string ConfigName { get; set; }

    /// <summary>明细结束方式：0 未配置，1 指定列最后有值行，2 指定汇总单元格所在行之前。</summary>
    [DbColumn(DefaultExpression = "0")]
    public int EndMode { get; set; }

    /// <summary>末行判断列的字母标识，例如 A；仅结束方式为 1 时保存。</summary>
    [DbColumn(StoreType = "varchar(3)")]
    public string? LastRowColumn { get; set; }

    /// <summary>汇总单元格的规范 A1 地址，例如 D30；仅结束方式为 2 时保存。</summary>
    [DbColumn(StoreType = "varchar(10)")]
    public string? SummaryCellAddress { get; set; }

    /// <summary>配置说明，不参与单元格读取。</summary>
    [DbColumn(StoreType = "varchar(500)")]
    public string? Remark { get; set; }
}


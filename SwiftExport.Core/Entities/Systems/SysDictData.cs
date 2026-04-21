using SwiftExport.Core.Common;

namespace SwiftExport.Core.Entities.Systems
{
    /// <summary>
    /// 字典明细实体
    /// 对应数据库表 SysDictData
    /// </summary>
    public class SysDictData : BaseEntity
    {
        /// <summary>
        /// 业务变更时间
        /// </summary>
        public DateTime? ChangeTime { get; set; }

        /// <summary>
        /// 字典编码（关联字典类型）
        /// </summary>
        public string? DictCode { get; set; }

        /// <summary>
        /// 英文名称
        /// </summary>
        public string? DataNameEn { get; set; }

        /// <summary>
        /// 中文名称
        /// </summary>
        public string? DataNameZh { get; set; }

        /// <summary>
        /// 备注
        /// </summary>
        public string? Remark { get; set; }

        /// <summary>
        /// 排序号
        /// </summary>
        public int? SortOrder { get; set; }

        /// <summary>
        /// 前端样式类名
        /// </summary>
        public string? CssClass { get; set; }

        /// <summary>
        /// 是否系统内置
        /// </summary>
        public bool IsSystem { get; set; }

        /// <summary>
        /// 是否启用
        /// </summary>
        public bool IsEnabled { get; set; }
    }
}

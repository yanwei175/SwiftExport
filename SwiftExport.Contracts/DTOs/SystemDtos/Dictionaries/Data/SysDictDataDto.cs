using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>传输字典明细及服务端内置身份；英文名称暂作业务代码，中文名称用于显示。</summary>
    public class SysDictDataDto : BaseDto
    {
        /// <summary>所属字典类型的稳定编码。</summary>
        public string DictCode { get; set; }
        /// <summary>英文名称；现有业务仍以此值作为明细代码。</summary>
        public string DataNameEn { get; set; }
        /// <summary>可维护的中文显示名称。</summary>
        public string DataNameZh { get; set; }
        /// <summary>业务备注。</summary>
        public string Remark { get; set; }
        /// <summary>可选显示顺序。</summary>
        public int? SortOrder { get; set; }
        /// <summary>可选显示样式类名。</summary>
        public string CssClass { get; set; }
        /// <summary>系统保护标记；旧系统记录仍按原有规则保护删除。</summary>
        public bool IsSystem { get; set; }
        /// <summary>是否由代码目录提供；服务端确定该事实，客户端不能通过提交标记授予身份。</summary>
        public bool IsBuiltIn { get; set; }
        /// <summary>普通明细的启用状态；内置明细始终启用。</summary>
        public bool IsEnabled { get; set; }
    }
}

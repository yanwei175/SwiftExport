using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>
    /// 视图路由配置 DTO
    /// 前后端数据传输对象，字段与实体保持一致（不暴露数据库内部字段）。
    /// </summary>
    public class SysViewRouteDto : BaseDto
    {
        /// <summary>路由键，唯一标识一条路由</summary>
        public string TargetViewKey { get; set; }

        /// <summary>ViewModel 完整类型名（含命名空间）</summary>
        public string ViewModelTypeName { get; set; }

        /// <summary>
        /// View 完整类型名（可选）。
        /// 为空时由 ViewLocatorSelector 按约定自动推断。
        /// </summary>
        public string ViewTypeName { get; set; }

        /// <summary>路由可读名称</summary>
        public string DisplayName { get; set; }

        /// <summary>是否启用</summary>
        public bool IsEnabled { get; set; }

        /// <summary>排序号</summary>
        public int SortOrder { get; set; }

        /// <summary>路由分类：Dict / Standalone 等</summary>
        public string RouteCategory { get; set; }

        /// <summary>程序集名称（多程序集扩展用，通常为空）</summary>
        public string AssemblyName { get; set; }

        /// <summary>备注</summary>
        public string Remark { get; set; }
    }
}

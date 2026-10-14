using SwiftExport.Contracts.DTOs.Base;
using System;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>传输字典类型与服务端内置身份事实，不承载客户端编辑状态。</summary>
    public class SysDictTypeDto : BaseDto
    {
        public string DictCode { get; set; }
        public string DictName { get; set; }
        public bool IsSystem { get; set; }

        /// <summary>该类型是否由代码目录提供；服务端维护此事实，客户端不能通过提交标记授予身份。</summary>
        public bool IsBuiltIn { get; set; }
        public string Remark { get; set; }

        // 新增：支撑树形结构
        public Guid? ParentId { get; set; }
        // 新增：支撑左侧树的排序
        public int SortOrder { get; set; }

        public string GroupCode { get; set; } // 如 "Basic" (基础档案), "Finance" (财务), "Sales" (销售)
        public string GroupName { get; set; } // 组名，如 "基础数据"



    }
}

using SwiftExport.Contracts.DTOs.Base;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    public class SysDictTypeDto : BaseDto
    {
        public string DictCode { get; set; }
        public string DictName { get; set; }
        public bool IsSystem { get; set; }
        public string Remark { get; set; }

        // 新增：支撑树形结构
        public int? ParentId { get; set; }
        // 新增：支撑左侧树的排序
        public int SortOrder { get; set; }

        // 新增：告诉前端右侧应该加载哪个 View
        public string TargetViewKey { get; set; }
        public string GroupCode { get; set; } // 如 "Basic" (基础档案), "Finance" (财务), "Sales" (销售)
        public string GroupName { get; set; } // 组名，如 "基础数据"



    }
}

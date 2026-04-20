using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>
    /// 字典类型分组树节点 DTO
    /// 用于返回按 GroupCode 分组的字典类型树结构
    /// 
    /// 结构：
    /// - 第一层：按 GroupCode 聚合的分组节点（Group）或未分组的单个字典类型（DictType）
    /// - 第二层+：基于 ParentId 的树形结构（不再按 GroupCode 聚合）
    /// </summary>
    public class DictTypeGroupTreeDto
    {
        /// <summary>
        /// 分组代码或字典类型 ID（取决于节点类型）
        /// - 当 GroupOrDictType = "Group" 时，值为 GroupCode
        /// - 当 GroupOrDictType = "DictType" 时，值为该字典类型的 ID（字符串格式）
        /// </summary>
        public string GroupCode { get; set; }

        /// <summary>
        /// 分组名称或字典类型名称（取决于节点类型）
        /// - 当 GroupOrDictType = "Group" 时，值为 GroupName
        /// - 当 GroupOrDictType = "DictType" 时，值为该字典类型的显示名称
        /// </summary>
        public string GroupName { get; set; }

        /// <summary>
        /// 节点类型：Group（分组） 或 DictType（字典类型）
        /// </summary>
        public string GroupOrDictType { get; set; }

        /// <summary>
        /// 当 GroupOrDictType = "DictType" 时，包含完整的字典类型数据
        /// 当 GroupOrDictType = "Group" 时，此字段为 null
        /// </summary>
        public SysDictTypeDto DictTypeData { get; set; }

        /// <summary>
        /// 子节点集合
        /// - 当 GroupOrDictType = "Group" 时，包含该分组下的所有字典类型树形结构
        /// - 当 GroupOrDictType = "DictType" 时，包含该字典类型的子节点（如果 ParentId = 该字典类型 ID）
        /// </summary>
        public List<DictTypeTreeDto> Children { get; set; } = new List<DictTypeTreeDto>();

        /// <summary>
        /// 排序编号
        /// </summary>
        public int SortOrder { get; set; }
    }
}

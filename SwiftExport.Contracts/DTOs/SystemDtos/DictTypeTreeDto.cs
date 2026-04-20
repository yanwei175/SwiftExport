using System.Collections.Generic;

namespace SwiftExport.Contracts.DTOs.SystemDtos
{
    /// <summary>
    /// 字典类型分组树节点 DTO
    /// 用于返回按 GroupCode 分组的字典类型树结构
    /// </summary>
    public class DictTypeTreeDto : SysDictTypeDto
    {
        /// <summary>
        /// 子节点集合（分组节点包含多个字典类型）
        /// </summary>
        public List<DictTypeTreeDto> Children { get; set; } = new List<DictTypeTreeDto>();


    }
}

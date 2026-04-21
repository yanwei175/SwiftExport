using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Services.Base;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using Mapster;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;


namespace SwiftExport.Application.Services.Systems
{
    /// <summary>
    /// 系统字典类型服务实现
    /// 支持标准 CRUD 操作和分组树查询
    /// </summary>
    public class SysDictTypeService(IRepository<SysDictType> repository, ServiceBaseContext context)
            : BaseService<SysDictType, SysDictTypeDto>(repository, context), ISysDictTypeService
    {
        /// <summary>
        /// 获取字典类型分组树
        /// 
        /// 算法：
        /// 1. 获取所有字典类型（未删除的）
        /// 2. 按 GroupCode 分组
        /// 3. 构建分组树（分组节点 + 字典类型子节点）
        /// 4. 按 SortOrder 排序
        /// </summary>
        public async Task<ServiceResult<List<DictTypeGroupTreeDto>>> GetDictTypeGroupTreeAsync()
        {
            try
            {
                // 1. 获取所有字典类型（仅用泛型 Repository 的 GetAllAsync）
                var allDictTypesReadOnly = await _repository.GetAllAsync();

                // GetAllAsync 返回值不可为 null，无需 null 判断，直接检查是否为空集合
                if (!allDictTypesReadOnly.Any())
                {
                    Logger.LogInformation("没有可用的字典类型数据");
                    return ServiceResult<List<DictTypeGroupTreeDto>>.Ok(new List<DictTypeGroupTreeDto>());
                }

                // 转换为 List
                var allDictTypes = allDictTypesReadOnly.ToList();

                // 2. 按 GroupCode 分组，组织树形结构
                var groupedTree = BuildGroupedTree(allDictTypes);

                Logger.LogInformation("成功构建字典类型分组树，共 {GroupCount} 个分组", groupedTree.Count);
                return ServiceResult<List<DictTypeGroupTreeDto>>.Ok(groupedTree);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "构建字典类型分组树失败");
                return ServiceResult<List<DictTypeGroupTreeDto>>.Fail($"构建分组树失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 构建分组树的方法
        /// 
        /// 核心逻辑：
        /// - 第一层：按 GroupCode 聚合成分组节点
        /// - 如果 GroupCode 为空，全部归入一个"未分组"节点
        /// - 第二层+：不再按 GroupCode 聚合，直接基于 ParentId 建立树形关系
        /// 
        /// 返回结构：
        /// 未分组
        ///   ├─ DictType1 (GroupCode = null)
        ///   └─ DictType2 (GroupCode = null)
        /// Group1
        ///   ├─ DictType3 (ParentId = null/0)
        ///   ├─ DictType4 (ParentId = null/0)
        ///   │   ├─ DictType4_1 (ParentId = DictType4.Id)
        ///   │   └─ DictType4_2 (ParentId = DictType4.Id)
        ///   └─ DictType5
        /// Group2
        ///   └─ ...
        /// </summary>
        private static List<DictTypeGroupTreeDto> BuildGroupedTree(List<SysDictType> allDictTypes)
        {
            // 1. 使用 Mapster 将 Entity 转换为 DTO
            var dictTypeDtos = allDictTypes.Adapt<List<SysDictTypeDto>>();

            // 2. 按 GroupCode 分组（第一层级聚合）
            var groupedByCode = dictTypeDtos
                .GroupBy(x => new { x.GroupCode, x.GroupName })
                .OrderBy(g => g.First().SortOrder)
                .ToList();

            var treeRoot = new List<DictTypeGroupTreeDto>();

            // 3. 创建未分组节点（用于收集所有 GroupCode 为空的字典类型）
            DictTypeGroupTreeDto ungroupedNode = null;

            foreach (var group in groupedByCode)
            {
                // 如果 GroupCode 为空，作为未分组的，加入统一的未分组节点
                if (string.IsNullOrEmpty(group.Key.GroupCode))
                {
                    // 首次创建未分组节点
                    if (ungroupedNode == null)
                    {
                        ungroupedNode = new DictTypeGroupTreeDto
                        {
                            GroupCode = "_UNGROUPED",
                            GroupName = "未分组",
                            GroupOrDictType = "Group",
                            SortOrder = 0,  // 未分组置于最前面
                            Children = new List<DictTypeTreeDto>()
                        };
                    }

                    // 将所有未分组的字典类型加入到未分组节点
                    var ungroupedDictTypes = group.ToList();
                    var ungroupedTree = BuildDictTypeTree(ungroupedDictTypes);
                    ungroupedNode.Children.AddRange(ungroupedTree);
                }
                else
                {
                    // 创建分组节点
                    var groupNode = new DictTypeGroupTreeDto
                    {
                        GroupCode = group.Key.GroupCode,
                        GroupName = group.Key.GroupName ?? group.Key.GroupCode,
                        GroupOrDictType = "Group",
                        SortOrder = group.First().SortOrder,
                        Children = new List<DictTypeTreeDto>()
                    };

                    // 为分组下的所有字典类型构建树形结构（第二层及以上基于 ParentId）
                    var groupDictTypes = group.ToList();
                    var dictTypeTree = BuildDictTypeTree(groupDictTypes);
                    groupNode.Children = dictTypeTree;

                    treeRoot.Add(groupNode);
                }
            }

            // 4. 如果有未分组的节点，添加到最前面
            if (ungroupedNode != null)
            {
                treeRoot.Insert(0, ungroupedNode);
            }

            // 最终按 SortOrder 排序
            return treeRoot.OrderBy(x => x.SortOrder).ToList();
        }

        /// <summary>
        /// 在树形结构中构建字典类型树的私有方法
        /// 
        /// 功能：
        /// - 基于 ParentId 和 Id 构建多层级树形结构
        /// - 支持无限级嵌套
        /// - 不再按 GroupCode 聚合（只在第一层做过聚合）
        /// 
        /// 使用场景：
        /// 1. 在分组内部构建树形结构
        /// 2. 支持分类树、导航树等需要多层级的场景
        /// </summary>
        private static List<DictTypeTreeDto> BuildDictTypeTree(List<SysDictTypeDto> dictTypes)
        {
            if (dictTypes.Count == 0)
            {
                return new List<DictTypeTreeDto>();
            }

            var rootNodes = new List<DictTypeTreeDto>();
            var nodeDict = dictTypes.ToDictionary(x => x.Id);
            var treeNodeDict = new Dictionary<int, DictTypeTreeDto>();

            // 1. 先创建所有树形节点
            foreach (var dictType in dictTypes.OrderBy(x => x.SortOrder))
            {
                var treeNode = new DictTypeTreeDto
                {
                    Id = dictType.Id,
                    DictCode = dictType.DictCode,
                    DictName = dictType.DictName,
                    IsSystem = dictType.IsSystem,
                    Remark = dictType.Remark,
                    ParentId = dictType.ParentId,
                    SortOrder = dictType.SortOrder,
                    TargetViewKey = dictType.TargetViewKey,
                    GroupCode = dictType.GroupCode,
                    GroupName = dictType.GroupName,
                    Children = new List<DictTypeTreeDto>()
                };

                treeNodeDict[dictType.Id] = treeNode;
            }

            // 2. 建立父子关系
            foreach (var dictType in dictTypes.OrderBy(x => x.SortOrder))
            {
                var treeNode = treeNodeDict[dictType.Id];

                // 判断是否为根节点（ParentId 为 null 或 0，或指向不存在的节点）
                bool isRootNode = dictType.ParentId == null || 
                                  dictType.ParentId == 0 || 
                                  !nodeDict.ContainsKey(dictType.ParentId.Value);

                if (isRootNode)
                {
                    rootNodes.Add(treeNode);
                }
                else
                {
                    // 为子节点，添加到父节点
                    var parentId = dictType.ParentId.Value;
                    if (treeNodeDict.ContainsKey(parentId))
                    {
                        treeNodeDict[parentId].Children.Add(treeNode);
                    }
                }
            }

            return rootNodes.OrderBy(x => x.SortOrder).ToList();
        }

        // 🚀 你可以在这里重写钩子函数
        // 比如：删除字典大类前，校验下面是不是还有明细数据
        protected override async Task<string?> ValidateBeforeDeleteAsync(List<int> ids)
        {
            // var hasData = await _dataRepository.ExistsAsync(...);
            // if (hasData) return "该字典分类下还有明细数据，禁止删除！";
            return await base.ValidateBeforeDeleteAsync(ids);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace SwiftExport.View.FW.Mvvm
{
    /// <summary>
    /// 树形模型服务接口
    /// 
    /// 核心职责：
    /// 1. 强制要求实现类提供 ParentId 字段验证
    /// 2. 提供树形数据的加载、构建、转换等业务逻辑
    /// 3. 支持 TreeModel 与 DTO 的映射
    /// </summary>
    public interface ITreeModelService<TTreeModel>
        where TTreeModel : BaseTreeNodeModel<TTreeModel>, new()
    {
        /// <summary>
        /// 加载树形数据并构建树结构
        /// </summary>
        Task<List<TTreeModel>> LoadAndBuildTreeAsync();

        /// <summary>
        /// 从扁平列表构建树形结构
        /// （此方法应验证 ParentId 字段的存在和有效性）
        /// </summary>
        Task<List<TTreeModel>> BuildTreeFromListAsync(List<TTreeModel> flatList);

        /// <summary>
        /// 保存整个树形结构（包括新增、修改、删除的节点）
        /// </summary>
        Task<bool> SaveTreeAsync(List<TTreeModel> treeNodes);

        /// <summary>
        /// 删除节点及其所有子节点
        /// </summary>
        Task<bool> DeleteNodeAndDescendantsAsync(int nodeId);

        /// <summary>
        /// 验证树形结构的完整性和一致性
        /// （检查 ParentId 引用、循环依赖、孤立节点等）
        /// </summary>
        Task<(bool IsValid, string ErrorMessage)> ValidateTreeStructureAsync(List<TTreeModel> nodes);
    }

    /// <summary>
    /// 简化版：不需要复杂业务的基础树形服务接口
    /// 只提供数据加载和树形构建
    /// </summary>
    public interface ISimpleTreeModelService<TTreeModel>
        where TTreeModel : BaseTreeNodeModel<TTreeModel>
    {
        /// <summary>
        /// 获取所有树形节点数据（扁平列表）
        /// 实现类负责验证 ParentId 字段存在
        /// </summary>
        Task<List<TTreeModel>> GetAllNodesAsync();

        /// <summary>
        /// 获取某个节点的子节点
        /// </summary>
        Task<List<TTreeModel>> GetChildrenAsync(int parentId);

        /// <summary>
        /// 获取树形节点的所有祖先节点（不含自身）
        /// </summary>
        Task<List<TTreeModel>> GetAncestorsAsync(int nodeId);
    }
}

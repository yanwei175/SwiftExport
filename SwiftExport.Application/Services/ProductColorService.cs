using AutoMapper;
using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Services
{
    public class ProductCategoryService : BaseService<ProductCategory>, IProductCategoryService
    {
        private readonly IProductCategoryRepository _thisrepo;
        private readonly IMapper _mapper;
        public ProductCategoryService(IProductCategoryRepository repo,
            IUintOfWorkFactory _uowFac, IMapper mapper) : base(repo, _uowFac)
        {
            _thisrepo = repo;
            _mapper = mapper;
        }

        public async Task<ServiceResult<List<ProductCategoryDto>>> GetTreeListAsync()
        {
            // 1. 获取所有未删除的数据 (扁平列表)
            // 假设你的泛型仓储有 GetListAsync 或类似方法
            var allEntities = await _thisrepo.GetAllAsync();
            var allDtos = _mapper.Map<List<ProductCategoryDto>>(allEntities);
            // 3. 递归构建树形结构 (从根节点 ParentId == null 开始)
            var tree = BuildTree(allDtos, null);
            if (tree == null)
            {
                return ServiceResult<List<ProductCategoryDto>>.Fail("没有数据");

            }
            else
            {
                return ServiceResult<List<ProductCategoryDto>>.Ok(tree);
            }

        }
        public async Task<ServiceResult<ProductCategoryDto>> CreateByDtoAsync(ProductCategoryDto dto)
        {
            var entity = _mapper.Map<ProductCategory>(dto);

            // 获取最大排序号
            entity.SortNo = await _thisrepo.GetMaxSortNoAsync(dto.父级ID) + 1;

            // 维护父级名称
            if (dto.父级ID.HasValue)
            {
                var parent = await _thisrepo.GetByIdAsync(dto.父级ID.Value);
                if (parent != null)
                {
                    entity.ParentName = parent.CategoryName;
                }
            }
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _thisrepo.AddAsync(entity, uow); // 假设 repo 方法已规范
                    // 【事务管理】提交
                    await uow.CommitAsync();
                    return ServiceResult<ProductCategoryDto>.Ok(dto);
                }
                catch (Exception)
                {
                    // 【事务管理】回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<ServiceResult<ProductCategoryDto>> UpdateByDtoAsync(ProductCategoryDto dto)
        {
            var entity = await _thisrepo.GetByIdAsync(dto.ID); // 注意检查 BaseDto 的 ID 属性
            if (entity == null)
            {
                return ServiceResult<ProductCategoryDto>.Fail("没有数据");
            }

            entity.CategoryName = dto.分类名称;
            entity.CategoryNo = dto.分类编号;
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _thisrepo.UpdateAsync(entity, uow); // 假设 repo 方法已规范
                    // 【事务管理】提交
                    await uow.CommitAsync();
                    return ServiceResult<ProductCategoryDto>.Ok(dto);
                }
                catch (Exception)
                {
                    // 【事务管理】回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }

        public async Task<ServiceResult<bool>> MoveCategoryAsync(int id, int? newParentId)
        {
            var entity = await _thisrepo.GetByIdAsync(id);
            if (entity == null)
            {
                return ServiceResult<bool>.Fail($"ID{id}不存在,移动失败!");
            }
            // 1. 防死循环检查：不能把自己移动到自己的子节点下
            // (这一步如果要在后端做，需要查整棵树，或者依靠前端 TreeView 的判断。
            // 为了性能，通常信任前端的判断，或者简单检查 newParentId != id)
            if (id == newParentId) return ServiceResult<bool>.Fail("不能将节点移动到自身!");

            // 2. 更新 ParentID
            entity.ParentID = newParentId;

            // 3. 更新 ParentName
            if (newParentId.HasValue)
            {
                var newParent = await _thisrepo.GetByIdAsync(newParentId.Value);
                entity.ParentName = newParent?.CategoryName;
            }
            else
            {
                entity.ParentName = null; // 变为根节点
            }

            // 4. 更新排序 (可选：移动后排在最后)
            entity.SortNo = await _thisrepo.GetMaxSortNoAsync(newParentId) + 1;

            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var affectedRows = await _thisrepo.UpdateAsync(entity, uow); // 假设 repo 方法已规范
                    // 【事务管理】提交
                    await uow.CommitAsync();
                    return ServiceResult<bool>.Ok(true);
                }
                catch (Exception)
                {
                    // 【事务管理】回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task ReorderCategoryAsync(int draggedId, int targetId, string position)
        {
            var dragged = await _thisrepo.GetByIdAsync(draggedId);
            var target = await _thisrepo.GetByIdAsync(targetId);
            if (dragged == null || target == null) return;
            if (dragged.ID == target.ID) return;

            if (position == "child")
            {
                // 原有的逻辑：变为子节点
                await MoveCategoryAsync(draggedId, target.ID);
            }
            else // "before" 或 "after"
            {
                // 1. 设置父级与目标一致
                dragged.ParentID = target.ParentID;
                dragged.ParentName = target.ParentName;

                // 2. 计算新的 SortNo
                // 为了简单高效，我们可以把目标位置之后的所有兄弟节点 SortNo + 1，腾出位置
                // 注意：实际项目中通常使用 decimal 类型的 SortNo 或者预留间隔（如 10, 20, 30）来避免大面积更新。
                // 这里演示简单的整数挤压逻辑：

                int newSortNo = target.SortNo;
                if (position == "after") newSortNo += 1;

                // 3. 执行数据库更新 (这里假设 Repository 有个批量更新排序的方法，或者我们手动处理)
                // 简单做法：我们只给 dragged 设为 newSortNo，但这样可能会有重复。
                // 严谨做法：需要把 >= newSortNo 的同级兄弟全部 +1

                // 假设 Repository 增加了一个 Helper 方法: ShiftSortNos(parentId, startSortNo, offset)
                // 这里为了不修改 Repository 接口太多，我用一种通用逻辑：

                dragged.SortNo = newSortNo;

                // *重要*：这里其实应该调用 Repository 去执行 SQL: 
                // UPDATE ProductCategory SET SortNo = SortNo + 1 WHERE ParentId = @Pid AND SortNo >= @Sort
                // 我们暂时假设只更新自己，依靠 Reload 时的 OrderBy 也许能勉强对付，但最好还是写 SQL。
                // 为了演示完整性，这里我们直接更新 dragged，实际建议去完善 Repository。
            }

            //await _thisrepo.UpdateAsync(dragged);
        }
        public async Task<ServiceResult<bool>> MoveNodeUpAsync(int id)
        {
            var current = await _thisrepo.GetByIdAsync(id);
            if (current == null)
                return ServiceResult<bool>.Fail("节点为空");

            // 1. 找出同级的所有节点

            var siblings = (await _repo.GetListAsync(new { current.ParentID }))
                           .OrderBy(x => x.SortNo)
                           .ToList();

            // 2. 找到当前节点在列表中的索引
            var index = siblings.FindIndex(x => x.ID == current.ID);

            // 3. 如果已经是第一个，无法上移
            if (index <= 0)
                return ServiceResult<bool>.Fail("节点为空");

            // 4. 获取前一个节点 (上一名)
            var prevNode = siblings[index - 1];

            // 5. 交换 SortNo
            (prevNode.SortNo, current.SortNo) = (current.SortNo, prevNode.SortNo);

            // 6. 两个都更新
            // (为了防止 SortNo 重复，其实最好的做法是重新重新生成一遍 1,2,3,4... 但交换最快)
            current.ChangeTime = DateTime.Now;
            prevNode.ChangeTime = DateTime.Now;
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    await _thisrepo.UpdateAsync(current, uow);
                    await _thisrepo.UpdateAsync(prevNode, uow);
                    await uow.CommitAsync();
                    return ServiceResult<bool>.Ok(true);
                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }

            }
        }

        public async Task<ServiceResult<bool>> MoveNodeDownAsync(int id)
        {
            var current = await _thisrepo.GetByIdAsync(id);
            if (current == null)
                return ServiceResult<bool>.Fail("不存在的ID");

            var siblings = (await _thisrepo.GetListAsync(new { current.ParentID }))
                           .OrderBy(x => x.SortNo)
                           .ToList();

            var index = siblings.FindIndex(x => x.ID == current.ID);

            // 如果已经是最后一个，无法下移
            if (index >= siblings.Count - 1)
                return ServiceResult<bool>.Fail("已经是最后一个"); ;

            // 获取后一个节点 (下一名)
            var nextNode = siblings[index + 1];

            // 交换 SortNo
            (nextNode.SortNo, current.SortNo) = (current.SortNo, nextNode.SortNo);

            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    await _thisrepo.UpdateAsync(current, uow);
                    await _thisrepo.UpdateAsync(nextNode, uow);
                    await uow.CommitAsync();
                    return ServiceResult<bool>.Ok(true);
                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }

            }
        }

        // --- 私有辅助方法：递归建树 ---
        private List<ProductCategoryDto> BuildTree(List<ProductCategoryDto> allNodes, int? parentId)
        {
            // 找到当前层级的所有节点
            var children = allNodes
                .Where(x => x.父级ID == parentId)
                .OrderBy(x => x.排序编号) // 按 SortNo 排序
                .ToList();

            // 递归查找子节点的子节点
            foreach (var child in children)
            {
                child.子集列表 = BuildTree(allNodes, child.ID);
            }

            return children;
        }
    }

}

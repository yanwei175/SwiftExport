using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Factory;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace SwiftExport.AppLayer.Services
{
    public class BaseService<T> : IBaseService<T> where T : BaseEntity
    {
        protected readonly IRepository<T> _repo;
        protected readonly IUintOfWorkFactory _uowFac;
        public BaseService(IRepository<T> repo, IUintOfWorkFactory uowFac)
        {
            _repo = repo;
            _uowFac = uowFac;
        }
        public async Task<bool> ExistsIdAsync(int id)
        {
            return await _repo.ExistsIdAsync(id);
        }
        public async Task<ServiceResult<IReadOnlyList<int>>> GetExistingIdsAsync(IEnumerable<int> ids)
        {
            // 1. 检查集合是否为空或 null
            if (ids == null || !ids.Any())
            {
                return ServiceResult<IReadOnlyList<int>>.Fail("传入的ID列表不能为空.");
            }
            // 2. 检查集合中是否存在任何一个无效ID（<= 0）
            if (ids.Any(id => id <= 0))
            {
                // 如果需要更详细的错误信息，可以找出是哪个无效 ID
                return ServiceResult<IReadOnlyList<int>>.Fail("ID列表中包含无效ID（ID必须大于0）.");
            }

            var rst = await _repo.GetExistingIdsAsync(ids);

            if (rst == null || !rst.Any())
            {
                return ServiceResult<IReadOnlyList<int>>.Fail("所有传入的ID在数据库中都不存在.");
            }
            return ServiceResult<IReadOnlyList<int>>.Ok(rst.ToList());
        }
        public async Task<ServiceResult<IReadOnlyList<int>>> GetNonExistingIDAsync(IEnumerable<int> ids)
        {
            // 1. 检查集合是否为空或 null
            if (ids == null || !ids.Any())
            {
                return ServiceResult<IReadOnlyList<int>>.Fail("传入的ID列表不能为空.");
            }
            // 2. 检查集合中是否存在任何一个无效ID（<= 0）
            if (ids.Any(id => id <= 0))
            {
                // 如果需要更详细的错误信息，可以找出是哪个无效 ID
                return ServiceResult<IReadOnlyList<int>>.Fail("ID列表中包含无效ID（ID必须大于0）.");
            }

            // 调用 Repository 获取数据库中已存在的 ID
            var existingIds = await _repo.GetExistingIdsAsync(ids);
            //如果所有的都不存在,直接返回原来的ID
            if (existingIds == null || !existingIds.Any())
            {
                return ServiceResult<IReadOnlyList<int>>.Ok(ids.ToList());
            }

            // int 类型默认使用数值比较，不需要额外的比较器。
            var allidSet = new HashSet<int>(ids);
            var existingidSet = new HashSet<int>(existingIds);

            // 使用 Except 方法计算不存在的集合。
            // Except 方法在 HashSet 上执行效率最高，因为它使用了底层的哈希查找。
            var nonExistentIds = allidSet.Except(existingidSet).ToList();
            return ServiceResult<IReadOnlyList<int>>.Ok(nonExistentIds);
        }
        public async Task<ServiceResult<T>> AddAsync(T entity)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (await ExistsIdAsync(entity.ID))
                    {
                        return ServiceResult<T>.Fail($"ID{entity.ID}已存在,不允许添加!");
                    }

                    // uow 开始
                    var newId = await _repo.AddAsync(entity, uow);

                    // uow 提交
                    await uow.CommitAsync();

                    return ServiceResult<T>.Ok(await _repo.GetByIdAsync(entity.ID));
                }
                catch (Exception)
                {
                    // uow 回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<ServiceResult<T>> UpdateAsync(T entity)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (!await ExistsIdAsync(entity.ID))
                    {
                        return ServiceResult<T>.Fail($"ID{entity.ID}不存在,不允许更新!");
                    }
                    
                    //uow 开始
                    var affectedRows = await _repo.UpdateAsync(entity, uow);

                    // uow 提交
                    await uow.CommitAsync();
                    return ServiceResult<T>.Ok(await _repo.GetByIdAsync(entity.ID));
                }
                catch (Exception)
                {  
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<ServiceResult<bool>> DeleteAsync(int id)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (!await ExistsIdAsync(id))
                    {
                        return ServiceResult<bool>.Fail($"ID{id}不存在,删除失败!");
                    }
                    // uow开始
                    var affectedRows = await _repo.DeleteAsync(id, uow);
                    // uow提交
                    await uow.CommitAsync();
                    return ServiceResult<bool>.Ok(true,"删除成功");
                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<ServiceResult<bool>> DeleteSoftAsync(int id)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (!await ExistsIdAsync(id))
                    {
                        return ServiceResult<bool>.Fail($"ID{id}不存在, 禁用失败!");
                    }

                    var affectedRows = await _repo.DeleteSoftAsync(id, uow);

                    // 事务提交
                    await uow.CommitAsync();
                    return ServiceResult<bool>.Ok(true, "禁用成功");

                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<ServiceResult<IReadOnlyList<T>>> GetAllAsync()
        {
            var rst= await _repo.GetAllAsync();
            if (rst == null)
            {
                return ServiceResult<IReadOnlyList<T>>.Fail("未找到数据!");
            }
           return ServiceResult<IReadOnlyList<T>>.Ok(rst.ToList());
        }
        public async Task<ServiceResult<T>> GetByIdAsync(int id)
        {
            if (!await ExistsIdAsync(id))
            {
                return ServiceResult<T>.Fail($"ID{id}不存在!");
            }
            if (id <= 0)
            {
                return ServiceResult<T>.Fail("传入的ID无效!");
            }
            var rst=await _repo.GetByIdAsync(id);
            if (rst == null)
            {
                return ServiceResult<T>.Fail($"ID{id}不存在!");
            }
            return ServiceResult<T>.Ok(rst);
        }

        /// <summary>
        /// 动态SQL查询
        /// </summary>
        /// <param name="条件字典">
        /// 示例：dic("ID",new QueryCondition(QueryOperator.等于,556))
        /// 字典的Key为待查询的数据库字段名，value为QueryCondition对象,对象里是 比对条件,条件值
        /// </param>
        /// <returns>一个对象列表,如果条件为空则返回所有IsDelete<>1的数据</returns>
        public async  Task<ServiceResult<IReadOnlyList<T>>> GetByUserWhereAsync(Dictionary<string, QueryCondition> 条件字典)
        {
            //遍历条件字典里的QueryCondition对象
            foreach (var kvp in 条件字典)
            { 
                string field = kvp.Key;
                if (string.IsNullOrEmpty(field.Trim()) ||  field.Trim().Length == 0 ||  field == "--请选择--")
                {
                    条件字典.Remove (field);
                }
            }

            var rst = await _repo.GetByUserWhereAsync(条件字典);
            if (rst == null)
            {
                return ServiceResult<IReadOnlyList<T>>.Fail("未找到数据!");
            }
            return ServiceResult<IReadOnlyList<T>>.Ok(rst.ToList());
        }

        public async Task<ServiceResult<T>> GetSingleEntityAsync()
        {
            var rst=await _repo.GetSingleEntityAsync();
            if (rst == null)
            {
                return ServiceResult<T>.Fail("未找到数据!");
            }
            return ServiceResult<T>.Ok(rst);
        }
        public async Task<ServiceResult<CUDResult>> BatchCUDByOnlyPkUniqueAsync(IEnumerable<T> addList,IEnumerable<T> updateList,IEnumerable<int> deleteIds)
        {
            // 1. 开启事务
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    var result = new CUDResult();

                    // 2. 执行删除 (使用 .ToList() 防止 IEnumerable 被多次迭代，且确保非空)
                    var delIds = deleteIds?.Where(id => id > 0).ToList();
                    if (delIds != null && delIds.Any())
                    {
                        result.AffectedRowsTotal += await _repo.DeleteRangeByIdsAsync(delIds, uow);
                        result.DeleteIIds.AddRange(delIds);
                    }

                    // 3. 执行新增 (此时 addList 里的实体 ID 是 0)
                    var adds = addList?.ToList();
                    if (adds != null && adds.Any())
                    {
                        // 这里利用了你仓库底层 INSERT OUTPUT 和反射回写 ID 的能力
                        var newIds = await _repo.AddRangeAsync(adds, uow);
                        result.AffectedRowsTotal += newIds.Count();
                        result.AddIIds.AddRange(newIds);
                    }

                    // 4. 执行修改
                    var updates = updateList?.ToList();
                    if (updates != null && updates.Any())
                    {
                        result.AffectedRowsTotal += await _repo.UpdateRangeByIdsAsync(updates, uow);
                        // 使用反射或特定的 ID 属性获取更新项的 ID
                        var idProp = typeof(T).GetProperty("ID");
                        var upIds = updates.Select(x => (int)idProp.GetValue(x)).ToList();
                        result.UpdateIIds.AddRange(upIds);
                    }

                    // 5. 提交事务
                    await uow.CommitAsync();
                    return ServiceResult<CUDResult>.Ok(result);
                }
                catch (Exception ex)
                {
                    await uow.RollbackAsync();
                    // 这里建议记录详细日志，然后返回友好提示
                    return ServiceResult<CUDResult>.Fail("批量保存失败：" + ex.Message);
                }
            }
        }
        public async Task<ServiceResult<IReadOnlyList<T>>> GetByIdsAsync(IEnumerable<int> ids)
        {
            if (ids == null || !ids.Any())
                return ServiceResult<IReadOnlyList<T>>.Fail("请提供有效的ID列表");
            var rst= await _repo.GetByIdsAsync(ids);
            if (rst==null)
                return ServiceResult<IReadOnlyList<T>>.Fail("未找到任何数据");

            return ServiceResult<IReadOnlyList<T>>.Ok(rst.ToList());
        }


    }
}

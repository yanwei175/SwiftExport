using SwiftExport.AppLayer.DTOs;
using SwiftExport.AppLayer.Interfaces;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
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
        public async Task<Result<IReadOnlyList<int>>> GetExistingIdsAsync(IEnumerable<int> ids)
        {
            // 1. 检查集合是否为空或 null
            if (ids == null || !ids.Any())
            {
                return Result<IReadOnlyList<int>>.Fail("传入的ID列表不能为空.");
            }
            // 2. 检查集合中是否存在任何一个无效ID（<= 0）
            if (ids.Any(id => id <= 0))
            {
                // 如果需要更详细的错误信息，可以找出是哪个无效 ID
                return Result<IReadOnlyList<int>>.Fail("ID列表中包含无效ID（ID必须大于0）.");
            }

            var rst = await _repo.GetExistingIdsAsync(ids);

            if (rst == null || !rst.Any())
            {
                return Result<IReadOnlyList<int>>.Fail("所有传入的ID在数据库中都不存在.");
            }
            return Result<IReadOnlyList<int>>.Ok(rst.ToList());
        }
        public async Task<Result<IReadOnlyList<int>>> GetNonExistingIDAsync(IEnumerable<int> ids)
        {
            // 1. 检查集合是否为空或 null
            if (ids == null || !ids.Any())
            {
                return Result<IReadOnlyList<int>>.Fail("传入的ID列表不能为空.");
            }
            // 2. 检查集合中是否存在任何一个无效ID（<= 0）
            if (ids.Any(id => id <= 0))
            {
                // 如果需要更详细的错误信息，可以找出是哪个无效 ID
                return Result<IReadOnlyList<int>>.Fail("ID列表中包含无效ID（ID必须大于0）.");
            }

            // 调用 Repository 获取数据库中已存在的 ID
            var existingIds = await _repo.GetExistingIdsAsync(ids);
            //如果所有的都不存在,直接返回原来的ID
            if (existingIds == null || !existingIds.Any())
            {
                return Result<IReadOnlyList<int>>.Ok(ids.ToList());
            }

            // int 类型默认使用数值比较，不需要额外的比较器。
            var allidSet = new HashSet<int>(ids);
            var existingidSet = new HashSet<int>(existingIds);

            // 使用 Except 方法计算不存在的集合。
            // Except 方法在 HashSet 上执行效率最高，因为它使用了底层的哈希查找。
            var nonExistentIds = allidSet.Except(existingidSet).ToList();
            return Result<IReadOnlyList<int>>.Ok(nonExistentIds);
        }
        public async Task<Result<int>> AddAsync(T entity)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (await ExistsIdAsync(entity.ID))
                    {
                        return Result<int>.Fail($"ID{entity.ID}已存在,不允许添加!");
                    }

                    // uow 开始
                    var newId = await _repo.AddAsync(entity, uow);

                    // uow 提交
                    await uow.CommitAsync();
                    return Result<int>.Ok(newId);
                }
                catch (Exception)
                {
                    // uow 回滚
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<Result<int>> UpdateAsync(T entity)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (!await ExistsIdAsync(entity.ID))
                    {
                        return Result<int>.Fail($"ID{entity.ID}不存在,不允许更新!");
                    }
                    
                    //uow 开始
                    var affectedRows = await _repo.UpdateAsync(entity, uow);

                    // uow 提交
                    await uow.CommitAsync();
                    return Result<int>.Ok(affectedRows);
                }
                catch (Exception)
                {  
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<Result<int>> DeleteAsync(int id)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (!await ExistsIdAsync(id))
                    {
                        return Result<int>.Fail($"ID{id}不存在,删除失败!");
                    }
                    // uow开始
                    var affectedRows = await _repo.DeleteAsync(id, uow);
                    // uow提交
                    await uow.CommitAsync();
                    return Result<int>.Ok(affectedRows);
                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<Result<int>> DeleteSoftAsync(int id)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                try
                {
                    if (!await ExistsIdAsync(id))
                    {
                        return Result<int>.Fail($"ID{id}不存在, 禁用失败!");
                    }

                    var affectedRows = await _repo.DeleteSoftAsync(id, uow);

                    // 事务提交
                    await uow.CommitAsync();
                    return Result<int>.Ok(affectedRows);

                }
                catch (Exception)
                {
                    await uow.RollbackAsync();
                    throw;
                }
            }
        }
        public async Task<Result<IReadOnlyList<T>>> GetAllAsync()
        {
            var rst= await _repo.GetAllAsync();
            if (rst == null)
            {
                return Result<IReadOnlyList<T>>.Fail("未找到数据!");
            }
           return Result<IReadOnlyList<T>>.Ok(rst.ToList());
        }
        public async Task<Result<T>> GetByIdAsync(int id)
        {
            if (!await ExistsIdAsync(id))
            {
                return Result<T>.Fail($"ID{id}不存在!");
            }
            if (id <= 0)
            {
                return Result<T>.Fail("传入的ID无效!");
            }
            var rst=await _repo.GetByIdAsync(id);
            if (rst == null)
            {
                return Result<T>.Fail($"ID{id}不存在!");
            }
            return Result<T>.Ok(rst);
        }

        /// <summary>
        /// 动态SQL查询
        /// </summary>
        /// <param name="条件字典">
        /// 示例：dic("ID",new QueryCondition(QueryOperator.等于,556))
        /// 字典的Key为待查询的数据库字段名，value为QueryCondition对象,对象里是 比对条件,条件值
        /// </param>
        /// <returns>一个对象列表,如果条件为空则返回所有IsDelete<>1的数据</returns>
        public async  Task<Result<IReadOnlyList<T>>> GetByUserWhereAsync(Dictionary<string, QueryCondition> 条件字典)
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
                return Result<IReadOnlyList<T>>.Fail("未找到数据!");
            }
            return Result<IReadOnlyList<T>>.Ok(rst.ToList());
        }

        public async Task<Result<T>> GetSingleEntityAsync()
        {
            var rst=await _repo.GetSingleEntityAsync();
            if (rst == null)
            {
                return Result<T>.Fail("未找到数据!");
            }
            return Result<T>.Ok(rst);
        }
        public async Task<Result<CUDResult>> BeachCUDByOnlyPkUniqueAsync(IEnumerable<T> createList, IEnumerable<T> updateList, IEnumerable<T> deleteList)
        {
            using (var uow = _uowFac.CreateUoW())
            {
                int totalRowsAffected = 0;
                CUDResult cUDResult = new CUDResult();
                try
                {
                    List<int> WrongAddId = new List<int>();
                    List<int> WrongUpdateId = new List<int>();
                    List<int> WrongDeleteId = new List<int>();
                    List<int> NeedUpdateIDs = new List<int>();
                    List<int> NeedDeleteIDs = new List<int>();
                    // 1. 检查新增的数据
                    if (createList != null && createList.Any())
                    {
                        foreach (var entity in createList)
                        {
                            if (entity.ID>0)
                                WrongAddId.Add(entity.ID);
                        }
                    }

                    // 2. 检查更新的数据
                    if (updateList != null && updateList.Any())
                    {
                        // 遍历并校验 ID
                        foreach (var entity in updateList)
                        {
                            if (entity.ID <= 0)
                            {
                                WrongUpdateId.Add(entity.ID);
                            }
                            NeedUpdateIDs.Add(entity.ID);
                        }
                        // 调用 Repository 获取数据库中已存在的 ID
                        var existingIds = await _repo.GetExistingIdsAsync(NeedUpdateIDs);
                        //如果所有的都不存在
                        if (existingIds == null || !existingIds.Any())
                        {
                            WrongUpdateId.AddRange(NeedUpdateIDs);
                        }
                        else
                        {
                            // int 类型默认使用数值比较，不需要额外的比较器。
                            var allidSet = new HashSet<int>(NeedUpdateIDs);
                            var existingidSet = new HashSet<int>(existingIds);

                            // 使用 Except 方法计算不存在的集合。
                            // Except 方法在 HashSet 上执行效率最高，因为它使用了底层的哈希查找。
                            var nonExistentIds = allidSet.Except(existingidSet).ToList();
                            if (nonExistentIds.Any())
                                WrongUpdateId.AddRange(nonExistentIds);
                        }
                    }

                    // 3. 检查删除数据
                    if (deleteList != null && deleteList.Any())
                    {
                        foreach (var entity in deleteList)
                        {
                            // 校验实体ID是否有效
                            if (entity.ID <= 0)
                            {
                                WrongDeleteId.Add(entity.ID);
                            }
                            NeedDeleteIDs.Add(entity.ID);
                        }
                    }

                    // 如果有错误，则返回错误信息
                    if (WrongAddId.Any() || WrongUpdateId.Any() || WrongDeleteId.Any())
                    {
                        // 创建一个自定义异常，包含错误信息
                        var errorMessage = new StringBuilder();
                        if (WrongAddId.Any())
                            errorMessage.AppendLine($"新创建的实体不能有ID,ID会由数据库自动创建。ID: {string.Join(", ",WrongAddId)}");
                        if (WrongUpdateId.Any())
                            errorMessage.AppendLine($"批量更新列表包含无效ID，事务已终止。ID: {string.Join(", ", WrongUpdateId)}");
                        if (WrongDeleteId.Any())
                            errorMessage.AppendLine($"删除操作列表包含无效ID的实体。实体ID: {string.Join(", ", WrongDeleteId)}");

                        return Result<CUDResult>.Fail(errorMessage.ToString());
                    }

                    //uow 开始写数据库
                    //新增
                    if (createList.Any())
                    {
                        cUDResult.AddIIds = (await _repo.AddRangeAsync(createList, uow)).ToList();
                        totalRowsAffected += cUDResult.AddIIds.Count;
                    }

                    //更新
                    if (updateList.Any())
                    {
                        totalRowsAffected += await _repo.UpdateRangeByIdsAsync(updateList, uow);
                        cUDResult.UpdateIIds = NeedUpdateIDs;
                    }

                    //删除
                    if (NeedDeleteIDs.Any())
                    {
                        totalRowsAffected += await _repo.DeleteRangeByIdsAsync(NeedDeleteIDs, uow);
                        cUDResult.DeleteIIds = NeedDeleteIDs;
                    }
                    // uow 事务
                    await uow.CommitAsync();

                    cUDResult.AffectedRowsTotal = totalRowsAffected;
                    return Result<CUDResult>.Ok(cUDResult);
                }
                catch (Exception)
                {
                    // 任何操作失败，回滚整个事务
                    await uow.RollbackAsync();
                    // 记录日志...
                    throw;
                }
            }
        }
        public async Task<Result<IReadOnlyList<T>>> GetByIdsAsync(IEnumerable<int> ids)
        {
            if (ids == null || !ids.Any())
                return Result<IReadOnlyList<T>>.Fail("请提供有效的ID列表");
            var rst= await _repo.GetByIdsAsync(ids);
            if (rst==null)
                return Result<IReadOnlyList<T>>.Fail("未找到任何数据");

            return Result<IReadOnlyList<T>>.Ok(rst.ToList());
        }


    }
}

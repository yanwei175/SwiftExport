using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.AppLayer.Interfaces
{
    public interface IBaseService<T> where T : BaseEntity
    {
        #region 泛型查询服务
        /// <summary>
        ///泛型服务,查看一个ID是否存在
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个布尔值</returns>
        Task<bool> ExistsIdAsync(int id);
        /// <summary>
        ///泛型服务,查看一堆ID是否存在
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个存在的ID集合</returns>
        Task<ServiceResult<IReadOnlyList<int>>> GetExistingIdsAsync(IEnumerable<int> ids);
        /// <summary>
        ///泛型服务,查看一堆ID是否存在
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个不存在的ID集合</returns>
        Task<ServiceResult<IReadOnlyList<int>>> GetNonExistingIDAsync(IEnumerable<int> ids);

        /// <summary>
        /// 泛型服务,通过ID获取一个实体
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个实体</returns>
        Task<ServiceResult<T>> GetByIdAsync(int id);

        /// <summary>
        /// 泛型服务,获取单个实体,主要是为了拿字段名
        /// </summary>
        /// <returns>返回一个实体</returns>
        Task<ServiceResult<T>> GetSingleEntityAsync();

        /// <summary>
        /// 泛型服务,获取一个表里的所有数据
        /// </summary>
        /// <returns>返回实体的集合</returns>
        Task<ServiceResult<IReadOnlyList<T>>> GetAllAsync();

        /// <summary>
        /// 动态SQL查询
        /// </summary>
        /// <param name="条件字典">
        /// 示例：dic("ID",new QueryCondition(QueryOperator.等于,556))
        /// 字典的Key为待查询的数据库字段名，value为QueryCondition对象,对象里是 比对条件,条件值
        /// </param>
        /// <returns>一个对象列表,如果条件为空则返回所有IsDelete<>1的数据</returns>
        Task<ServiceResult<IReadOnlyList<T>>> GetByUserWhereAsync(Dictionary<string, QueryCondition> 条件字典);

        /// <summary>
        /// 泛型服务,通过ID获取一堆实体
        /// </summary>
        /// <param name="ids">
        /// 整数类型的主键ID列表
        /// </param>
        /// <returns>返回一个实体列表</returns>
        Task<ServiceResult<IReadOnlyList<T>>> GetByIdsAsync(IEnumerable<int> ids);
        #endregion

        #region 泛型增删改

        /// <summary>
        /// 泛型服务,新增一个实体
        /// </summary>
        /// <param name="entity">
        /// 实体对象
        /// </param>
        /// <returns>返回新的带ID的实体</returns>
        Task<ServiceResult<T>> AddAsync(T entity);


        /// <summary>
        /// 泛型服务,根据ID软删除
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回被删除的ID</returns>
        Task<ServiceResult<bool>> DeleteSoftAsync(int id);


        /// <summary>
        /// 泛型服务,根据ID硬删除
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回被删除的ID</returns>
        Task<ServiceResult<bool>> DeleteAsync(int id);


        /// <summary>
        /// 泛型服务,更新一个实体
        /// </summary>
        /// <param name="entity">
        /// 一个实体对象
        /// </param>
        /// <returns>返回更新后的实体,主要为了应对数据库里有触发器的情况</returns>
        Task<ServiceResult<T>> UpdateAsync(T entity);


        /// <summary>
        /// 泛型服务,批量增删改,适合只有主键唯一的情况
        /// </summary>
        /// <param name="createList">
        /// 新增的实体列表
        /// </param>
        /// <param name="updateList">
        /// 修改的实体列表
        /// </param>
        /// <param name="deleteList">
        /// 删除的实体列表
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<ServiceResult<CUDResult>> BatchCUDByOnlyPkUniqueAsync(IEnumerable<T> addList, IEnumerable<T> updateList, IEnumerable<int> deleteIds);
        #endregion



    }
}

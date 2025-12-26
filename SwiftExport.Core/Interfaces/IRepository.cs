using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;

namespace SwiftExport.Core.Interfaces
{

    public interface IRepository<T> where T : BaseEntity
    {

        /// <summary>
        ///泛型仓库,查看一个ID是否存在
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个布尔值</returns>
        Task<bool> ExistsIdAsync(int id);

        /// <summary>
        ///泛型仓库,查看一堆ID是否存在
        /// </summary>
        /// <param name="ids">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个存在的ID集合</returns>
        Task<IEnumerable<int>> GetExistingIdsAsync(IEnumerable<int> ids);

        /// <summary>
        /// 泛型仓库,通过ID获取一个实体
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个实体</returns>
        Task<T> GetByIdAsync(int id);

        /// <summary>
        /// 泛型仓库,通过ID获取一个实体
        /// </summary>
        /// <param name="ids">
        /// 整数类型的主键ID列表
        /// </param>
        /// <returns>返回一个实体列表</returns>
        Task<IEnumerable<T>> GetByIdsAsync(IEnumerable<int> ids);

        /// <summary>
        /// 泛型仓库,获取单个实体,主要是为了拿字段名
        /// </summary>
        /// <returns>返回一个实体</returns>
        Task<T> GetSingleEntityAsync();

        /// <summary>
        /// 泛型仓库,新增一个实体,并返回新增实体的主键ID
        /// </summary>
        /// <param name="entity">
        /// 实体对象
        /// </param>
        /// <returns>返回新ID</returns>
        Task<int> AddAsync(T entity,IUnitOfWork uow);

        /// <summary>
        /// 泛型仓库,根据ID软删除
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<int> DeleteSoftAsync(int id, IUnitOfWork uow);

        /// <summary>
        /// 泛型仓库,根据ID硬删除
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<int> DeleteAsync(int id, IUnitOfWork uow);

        /// <summary>
        /// 泛型仓库,更新一个实体
        /// </summary>
        /// <param name="entity">
        /// 一个实体对象
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<int> UpdateAsync(T entity, IUnitOfWork uow);

        /// <summary>
        /// 泛型仓库,获取一个表里的所有数据
        /// </summary>
        /// <returns>返回实体的集合</returns>
        Task<IEnumerable<T>> GetAllAsync();

        /// <summary>
        /// 泛型仓库,批量新增实体到数据库
        /// </summary>
        /// <param name="entities">
        /// 一批实体对象的列表
        /// </param>
        /// <returns>返回新的ID集合</returns>
        Task<IEnumerable<int>> AddRangeAsync(IEnumerable<T> entities, IUnitOfWork uow);

        /// <summary>
        /// 泛型仓库,硬删除一批实体通过ID列表
        /// </summary>
        /// <param name="ids">
        /// 主键ID的列表
        /// </param>
        /// <returns>受影响行数</returns>
        Task<int> DeleteRangeByIdsAsync(IEnumerable<int> ids, IUnitOfWork uow);

        /// <summary>
        /// 泛型仓库,批量更新一批实体
        /// </summary>
        /// <param name="entities">
        /// 一批实体对象列表
        /// </param>
        /// <returns>受影响行数</returns>
        Task<int> UpdateRangeByIdsAsync(IEnumerable<T> entities, IUnitOfWork uow);

        /// <summary>
        /// 动态SQL查询
        /// </summary>
        /// <param name="条件字典">
        /// 示例：dic("ID",new QueryCondition(QueryOperator.等于,556))
        /// 字典的Key为待查询的数据库字段名，value为QueryCondition对象,对象里是 比对条件,条件值
        /// </param>
        /// <returns>一个对象列表,如果条件为空则返回所有IsDelete<>1的数据</returns>
        Task<IEnumerable<T>> GetByUserWhereAsync(Dictionary<string, QueryCondition> 条件字典);

        /// <summary>
        /// 基础分页查询
        /// </summary>
        /// <param name="pageIndex">页码</param>
        /// <param name="pageSize">每页大小</param>
        /// <param name="whereSql">SQL 过滤条件 (如: Name LIKE @Key)</param>
        /// <param name="parameters">参数对象</param>
        /// <param name="orderBy">排序字段 (必填，因为 OFFSET 需要顺序)</param>
        Task<PagedResult<T>> GetPagedListAsync(int pageIndex, int pageSize, string whereSql, object parameters, string orderBy);



    }


}

using SwiftExport.Core.Common;
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
        /// 泛型仓库,通过ID获取一个实体
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回一个实体</returns>
        Task<T> GetSingleByIdAsync(int id);
        /// <summary>
        /// 泛型仓库,新增一个实体,并返回新增实体的主键ID
        /// </summary>
        /// <param name="entity">
        /// 实体对象
        /// </param>
        /// <returns>返回新增的实体的主键ID</returns>
        Task<int> AddSingleReturnNewIDAsync(T entity);
        /// <summary>
        /// 泛型仓库,根据ID软删除
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<int> DisableSingleByID(int id);
        /// <summary>
        /// 泛型仓库,根据ID硬删除
        /// </summary>
        /// <param name="id">
        /// 整数类型的主键ID
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<int> DeleteSingleByIdAsync(int id);
        /// <summary>
        /// 泛型仓库,更新一个实体
        /// </summary>
        /// <param name="entity">
        /// 一个实体对象
        /// </param>
        /// <returns>返回受影响的行数</returns>
        Task<int> UpdateSingleAsync(T entity);

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
        /// <returns>新增的实体对象列表</returns>
        Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities);

        /// <summary>
        /// 泛型仓库,硬删除一批实体通过ID列表
        /// </summary>
        /// <param name="ids">
        /// 主键ID的列表
        /// </param>
        /// <returns>受影响行数</returns>
        Task<int> DeleteRangeByIdsAsync(IEnumerable<int> ids);

        /// <summary>
        /// 泛型仓库,批量更新一批实体
        /// </summary>
        /// <param name="entities">
        /// 一批实体对象列表
        /// </param>
        /// <returns>受影响行数</returns>
        Task<int> UpdateRangeAsync(IEnumerable<T> entities);




    }


}

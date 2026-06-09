using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using System;

namespace SwiftExport.Application.Interfaces;

public interface IBaseService<TEntity, TDto> where TEntity : class where TDto : class
{
    // 基础查询
    Task<bool> ExistsIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<Guid>>> GetExistingIdListAsync(IEnumerable<Guid> uidList);
    Task<ServiceResult<TDto>> GetByIdAsync(Guid uid);
    Task<ServiceResult<IReadOnlyList<TDto>>> GetByIdListAsync(IEnumerable<Guid> uidList);
    Task<ServiceResult<TDto?>> GetSingleEntityAsync();
    Task<ServiceResult<IReadOnlyList<TDto>>> GetAllAsync();
    /// <summary>
    /// 自定义条件查询 (whereSql: "WHERE CustomerName = @CustomerName", param: new { CustomerName = "X" })
    /// </summary>
    /// <param name="whereSql"></param>
    /// <param name="param"></param>
    /// <returns>实体列表</returns>
    Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(string whereSql, object? param = null);
    /// <summary>
    /// 动态SQL查询
    /// </summary>
    /// <param name="criteria">
    /// 示例：QueryCriteria("ID",new QueryCondition(QueryOperator.等于,556))
    /// 字典的Key为待查询的数据库字段名，value为QueryCondition对象,对象里是 比对条件,条件值
    /// </param>
    /// <returns>一个对象列表,如果条件为空则返回所有IsDelete<>1的数据</returns>
    Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(QueryCriteria criteria);
    /// <summary>
    /// 通用查询：传入匿名对象，属性名对应字段名，值为相等条件
    /// </summary>
    Task<ServiceResult<IReadOnlyList<TDto>>> GetListAsync(object noNameObj);
    /// <summary>
    /// 基础分页查询
    /// </summary>
    /// <param name="pageIndex">页码</param>
    /// <param name="pageSize">每页大小</param>
    /// <param name="whereSql">SQL 过滤条件 (如: CustomerName LIKE @Key)</param>
    /// <param name="parameters">参数对象</param>
    /// <param name="orderBy">排序字段 (必填，因为 OFFSET 需要顺序)</param>
    Task<ServiceResult<PagedResultDto<TDto>>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Uid DESC");

    // 增删改 (返回 ID 或 受影响行数)
    Task<ServiceResult<Guid>> AddAsync(TDto dto);
    Task<ServiceResult<int>> UpdateAsync(TDto dto);
    Task<ServiceResult<int>> DeleteSoftAsync(Guid uid);
    Task<ServiceResult<int>> DeleteAsync(Guid uid);

    // 批量增删改 (返回 ID 或 受影响行数)
    Task<ServiceResult<IReadOnlyList<Guid>>> AddRangeAsync(IEnumerable<TDto> dtoList);
    Task<ServiceResult<int>> DeleteRangeAsync(IEnumerable<Guid> uidList);
    Task<ServiceResult<int>> DeleteRangeSoftAsync(IEnumerable<Guid> uidList);
    Task<ServiceResult<int>> UpdateRangeAsync(IEnumerable<TDto> dtoList);

    Task<ServiceResult<BatchResultDto>> BatchSaveAsync(BatchRequestDto<TDto> batchReq);

}
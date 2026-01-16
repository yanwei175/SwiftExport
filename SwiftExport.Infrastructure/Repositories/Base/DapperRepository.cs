using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Data;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Base;

public class DapperRepository<T>(DbSession session) : IRepository<T> where T : BaseEntity
{
    private readonly DbSession _session = session ?? throw new ArgumentNullException(nameof(session));
    protected static readonly string TableName = typeof(T).GetCustomAttribute<TableAttribute>()?.Name ?? typeof(T).Name;
    private static readonly List<string> _writeableProps = GetWriteableProperties();
    // 辅助属性：当前连接和事务
    protected IDbConnection Connection => _session.Connection;
    protected IDbTransaction? Transaction => _session.Transaction;

    // =============================================================
    // 利用反射一次性获取属性
    // =============================================================
    private static List<string> GetWriteableProperties()
    {
        // 这里的排除列表你可以根据自己的 BaseEntity 随意调整
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ID", "IsDelete", "CreateTime", "UpdateTime", "CreatedTime", "UpdatedTime"
            };

        return [.. typeof(T).GetProperties()
            .Where(p => p.CanWrite && !excluded.Contains(p.Name))
            .Select(p => p.Name)];
    }


    public virtual async Task<bool> ExistsIdAsync(int id)
    {
        var sql = $"SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) WHERE [ID] = @Id";
        return await Connection.ExecuteScalarAsync<int>(sql, new { Id = id }, transaction: Transaction) > 0;
    }
    public virtual async Task<IReadOnlyList<int>> GetExistingIdListAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return [];
        var sql = $"SELECT [ID] FROM [{TableName}] WITH(NOLOCK) WHERE [ID] IN @Ids";
        var rst = await Connection.QueryAsync<int>(sql, new { Ids = idList }, transaction: Transaction);
        return [.. rst];
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [ID] = @Id AND IsDeleted <> 1";
        return await Connection.QuerySingleOrDefaultAsync<T>(sql, new { Id = id }, transaction: Transaction);
    }

    public virtual async Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return [];
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [ID] IN @Ids AND IsDeleted <> 1";
        var rst = await Connection.QueryAsync<T>(sql, new { Ids = idList }, transaction: Transaction);
        return [.. rst];
    }


    public virtual async Task<T?> GetSingleEntityAsync()
    {
        var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK)";
        return await Connection.QueryFirstOrDefaultAsync<T>(sql, transaction: Transaction);
    }


    public virtual async Task<IReadOnlyList<T>> GetAllAsync()
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted <> 1";
        var rst = await Connection.QueryAsync<T>(sql, transaction: Transaction);
        return [.. rst];
    }
    public virtual async Task<IReadOnlyList<T>> GetListAsync(string whereSql, object? param = null)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted <> 1";
        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            sql += whereSql.TrimStart().StartsWith("WHERE", StringComparison.OrdinalIgnoreCase) ? $" {whereSql}" : $" WHERE {whereSql}";
        }
        var rst = await Connection.QueryAsync<T>(sql, param, transaction: Transaction);
        return [.. rst];
    }

    public virtual async Task<IReadOnlyList<T>> GetListAsync(object noNameObj)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted <> 1";
        if (noNameObj != null)
        {
            var props = noNameObj.GetType().GetProperties();
            if (props.Length > 0)
            {
                var conditions = string.Join(" AND ", props.Select(p => $"[{p.Name}] = @{p.Name}"));
                sql += $" AND {conditions}";
            }
        }
        var rst = await Connection.QueryAsync<T>(sql, noNameObj, transaction: Transaction);
        return [.. rst];
    }

    public virtual async Task<IReadOnlyList<T>> GetListAsync(QueryCriteria criteria)
    {
        var sb = new StringBuilder($"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted <> 1");
        var parameters = new DynamicParameters();

        // 调用下方的私有解析方法
        var whereClause = ParseQueryCriteria(criteria, parameters);
        sb.Append(whereClause);

        var result = await Connection.QueryAsync<T>(sb.ToString(), parameters, transaction: Transaction);
        return [.. result];
    }

    /// <summary>
    /// 泛型分页查询 (基于 QueryCriteria)
    /// </summary>
    /// <param name="criteria">查询条件封装对象</param>
    /// <param name="pageIndex">页码 (索引从1开始,内部会处理)</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="orderBy">排序字段 (例如 "ID DESC")，必填，否则分页会报错</param>
    /// <returns>分页结果</returns>
    public virtual async Task<PagedResult<T>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "ID DESC")
    {
        var sb = new StringBuilder("WHERE IsDeleted <> 1");
        var parameters = new DynamicParameters();

        // 1. 解析动态条件
        var whereClause = ParseQueryCriteria(criteria, parameters);
        sb.Append(whereClause);

        // 2. 计算分页偏移量
        if (pageIndex < 1) pageIndex = 1;
        int offset = (pageIndex - 1) * pageSize;

        parameters.Add("@Offset", offset);
        parameters.Add("@Limit", pageSize);

        // 3. 组装 SQL (SQL Server 2012+ 写法)
        // 一次查询返回总数和当前页数据，效率更高
        var sql = $@"
                SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) {sb};
                
                SELECT * FROM [{TableName}] WITH(NOLOCK) {sb} 
                ORDER BY {orderBy} 
                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
        var total = await multi.ReadFirstAsync<int>();
        var items = await multi.ReadAsync<T>();

        return new PagedResult<T>
        {
            Items = [.. items],
            TotalCount = total,
            PageIndex = pageIndex,
            PageSize = pageSize
        };
    }











    // =============================================================
    // 增删改 (Write) - 使用 Output 这里的 ID 为 int
    // =============================================================

    public virtual async Task<int> AddAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 使用静态缓存的属性列表，不用每次都反射
        var columns = string.Join(", ", _writeableProps.Select(p => $"[{p}]"));
        var paramsName = string.Join(", ", _writeableProps.Select(p => $"@{p}"));

        var sql = $@"INSERT INTO [{TableName}] ({columns}) VALUES ({paramsName});
                         SELECT CAST(SCOPE_IDENTITY() AS INT);";

        var newId = await Connection.ExecuteScalarAsync<int>(sql, entity, transaction: Transaction);

        // 回写 ID
        var idProp = typeof(T).GetProperty("ID");
        if (idProp != null && idProp.CanWrite) idProp.SetValue(entity, newId);

        return newId;
    }
    public virtual async Task<int> UpdateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 拼接 SET 语句
        var setClause = string.Join(", ", _writeableProps.Select(p => $"[{p}] = @{p}"));
        var sql = $"UPDATE [{TableName}] SET {setClause} WHERE [ID] = @ID";

        return await Connection.ExecuteAsync(sql, entity, transaction: Transaction);
    }

    public virtual async Task<int> DeleteSoftAsync(int id)
    {
        var sql = $"UPDATE [{TableName}] SET IsDelete = 1 WHERE [ID] = @Id";
        return await Connection.ExecuteAsync(sql, new { Id = id }, transaction: Transaction);
    }

    public virtual async Task<int> DeleteAsync(int id)
    {
        var sql = $"DELETE FROM [{TableName}] WHERE [ID] = @Id";
        return await Connection.ExecuteAsync(sql, new { Id = id }, transaction: Transaction);
    }

    // =============================================================
    // 批量操作 (Batch Write)
    // =============================================================
    public virtual async Task<IReadOnlyList<int>> AddRangeAsync(IEnumerable<T> entities)
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.ToList();
        if (entityList.Count == 0) return [];

        // 1. 预先获取 PropertyInfo
        // 这里的 _writeableProps 是我们在类里定义的静态 List<string>
        var props = _writeableProps
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var columns = string.Join(", ", _writeableProps.Select(c => $"[{c}]"));

        // 2. 构造 SQL 和 参数
        var valueRows = new List<string>();
        var parameters = new DynamicParameters();

        for (int i = 0; i < entityList.Count; i++)
        {
            var rowParams = new List<string>();
            foreach (var prop in props)
            {
                var paramName = $"p{i}_{prop.Name}";

                // 直接取值
                parameters.Add(paramName, prop.GetValue(entityList[i]));
                rowParams.Add($"@{paramName}");
            }
            valueRows.Add($"({string.Join(", ", rowParams)})");
        }

        var sql = $@"
        INSERT INTO [{TableName}] ({columns})
        OUTPUT INSERTED.ID
        VALUES {string.Join(", ", valueRows)};
    ";

        // 3. 执行 (直接用 Connection 和 Transaction 属性)
        var newIds = (await Connection.QueryAsync<int>(
                            sql,
                            parameters,
                            transaction: Transaction)).ToList();

        // 4. 回写 ID
        var idProperty = typeof(T).GetProperty("ID");
        if (idProperty != null && idProperty.CanWrite)
        {
            for (int i = 0; i < newIds.Count && i < entityList.Count; i++)
            {
                idProperty.SetValue(entityList[i], newIds[i]);
            }
        }

        return newIds;
    }

    public virtual async Task<int> DeleteRangeAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return 0;

        var sql = $"DELETE FROM [{TableName}] WHERE ID IN @IdList";

        // 直接用 Connection 和 Transaction 属性
        return await Connection.ExecuteAsync(sql, new { IdList = idList }, transaction: Transaction);
    }
    public virtual async Task<int> DeleteRangeSoftAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return 0;

        var sql = $"UPDATE [{TableName}] SET IsDeleted=1 WHERE ID IN @IdList";

        // 直接用 Connection 和 Transaction 属性
        return await Connection.ExecuteAsync(sql, new { IdList = idList }, transaction: Transaction);
    }
    public virtual async Task<int> UpdateRangeAsync(IEnumerable<T> entities)
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.ToList();
        if (entityList.Count == 0) return 0;

        // 1. 准备属性
        var updateProps = _writeableProps
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var idProp = typeof(T).GetProperty("ID") ?? throw new InvalidOperationException("实体必须包含 ID 属性才能进行批量更新");
        var parameters = new DynamicParameters();
        var sqlBuilder = new StringBuilder();

        for (int i = 0; i < entityList.Count; i++)
        {
            var setClauses = new List<string>();

            foreach (var prop in updateProps)
            {
                var paramName = $"p{i}_{prop.Name}";
                setClauses.Add($"[{prop.Name}] = @{paramName}");
                parameters.Add(paramName, prop.GetValue(entityList[i]));
            }

            var idParamName = $"p{i}_ID";
            parameters.Add(idParamName, idProp.GetValue(entityList[i]));

            sqlBuilder.AppendLine($"UPDATE [{TableName}] SET {string.Join(", ", setClauses)} WHERE [ID] = @{idParamName};");
        }

        // 直接用 Connection 和 Transaction 属性
        return await Connection.ExecuteAsync(sqlBuilder.ToString(), parameters, transaction: Transaction);
    }




    /// <summary>
    /// 辅助方法：生成单个条件的 SQL 片段，并填充参数
    /// </summary>
    private static string BuildConditionSql(string propName, QueryCondition cond, string paramName, DynamicParameters parameters)
    {
        string safeField = $"[{propName}]";

        // 2. 根据操作符生成 SQL
        switch (cond.操作符)
        {
            case 查询操作符.等于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} = {paramName}";

            case 查询操作符.模糊匹配:
                parameters.Add(paramName, $"%{cond.值1}%");
                return $"{safeField} LIKE {paramName}";

            case 查询操作符.大于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} > {paramName}";

            case 查询操作符.小于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} < {paramName}";

            case 查询操作符.大于等于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} >= {paramName}";

            case 查询操作符.小于等于:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} <= {paramName}";

            case 查询操作符.区间:
                string pStart = $"{paramName}_Start";
                string pEnd = $"{paramName}_End";
                parameters.Add(pStart, cond.值1);
                parameters.Add(pEnd, cond.值2);
                return $"{safeField} BETWEEN {pStart} AND {pEnd}";

            case 查询操作符.包含某值:
                parameters.Add(paramName, cond.值1);
                return $"{safeField} IN {paramName}";

            default:
                return "1=1"; // 容错
        }
    }

    /// <summary>
    /// 解析查询条件对象为 SQL
    /// </summary>
    protected string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters)
    {
        // 基本校验
        if (criteria == null || (criteria.Items.Count == 0 && criteria.OrGroups.Count == 0))
        {
            return string.Empty;
        }

        StringBuilder sqlBuilder = new();

        // =======================================================
        // Part 1: 处理固定 AND 条件 (criteria.Items)
        // =======================================================
        foreach (var kvp in criteria.Items)
        {
            string propName = kvp.Key;
            var cond = kvp.Value;
            string paramName = $"@{propName}"; // AND 条件直接用属性名做参数

            // 调用抽取的方法
            string sqlPart = DapperRepository<T>.BuildConditionSql(propName, cond, paramName, parameters);

            // 拼接到总 SQL (注意前面加 AND)
            sqlBuilder.Append($" AND {sqlPart}");
        }

        // =======================================================
        // Part 2: 处理 OR 分组 (criteria.OrGroups)
        // 逻辑：AND ( (A OR B) AND (C OR D) )
        // =======================================================
        int groupIndex = 0; // 用于生成唯一的参数名，防止不同组之间冲突

        foreach (var groupDict in criteria.OrGroups)
        {
            if (groupDict.Count == 0) continue;

            List<string> orParts = [];

            foreach (var kvp in groupDict)
            {
                string propName = kvp.Key;
                var cond = kvp.Value;

                // ★ 关键：OR 参数名必须唯一化，避免与 Part 1 或其他组冲突
                // 例如: @Or_0_SKU, @Or_0_Description
                string paramName = $"@Or_{groupIndex}_{propName}";

                // 调用抽取的方法
                string sqlPart = DapperRepository<T>.BuildConditionSql(propName, cond, paramName, parameters);

                orParts.Add(sqlPart);
            }

            // 如果这一组有有效的 SQL 片段
            if (orParts.Count > 0)
            {
                // 用 " OR " 连接组内元素，并用括号包裹整个组
                // 结果示例: AND ( [SKU] LIKE @... OR [Desc] LIKE @... )
                string groupSql = $"({string.Join(" OR ", orParts)})";

                sqlBuilder.Append($" AND {groupSql}");
            }

            groupIndex++;
        }

        return sqlBuilder.ToString();
    }





}
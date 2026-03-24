using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Reflection;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Base;

public partial class DapperRepository<T>(RepositoryBaseContext context) : IRepository<T> where T : BaseEntity
{
    private readonly DbSession _session = context.Session ?? throw new ArgumentNullException(nameof(context.Session));

    private ILogger? _loggerInstance;
    protected ILogger Logger => _loggerInstance ??= context.LoggerFactory.CreateLogger(GetType());

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
                "Id", "IsDeleted", "CreateTime", "UpdateTime", "CreatedTime", "UpdatedTime"
            };

        return [.. typeof(T).GetProperties()
            .Where(p => p.CanWrite && !excluded.Contains(p.Name))
            .Select(p => p.Name)];
    }


    public virtual async Task<bool> ExistsIdAsync(int id)
    {
        var sql = $"SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) WHERE [Id] = @Id";
        return await Connection.ExecuteScalarAsync<int>(sql, new { Id = id }, transaction: Transaction) > 0;
    }
    public virtual async Task<IReadOnlyList<int>> GetExistingIdListAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return [];
        var sql = $"SELECT [Id] FROM [{TableName}] WITH(NOLOCK) WHERE [Id] IN @Ids";
        var rst = await Connection.QueryAsync<int>(sql, new { Ids = idList }, transaction: Transaction);
        return [.. rst];
    }

    public virtual async Task<T?> GetByIdAsync(int id)
    {
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Id] = @Id AND IsDeleted <> 1";
        return await Connection.QuerySingleOrDefaultAsync<T>(sql, new { Id = id }, transaction: Transaction);
    }

    public virtual async Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return [];
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE [Id] IN @Ids AND IsDeleted <> 1";
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
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted <> 1 ORDER BY ISNULL(ChangeTime, CreateDate) DESC";
        var rst = await Connection.QueryAsync<T>(sql, transaction: Transaction);
        return [.. rst];
    }
    public virtual async Task<IReadOnlyList<T>> GetListAsync(string whereSql, object? param = null)
    {
        // 1. 基础 SQL 已经包含了 WHERE IsDeleted <> 1
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted <> 1";

        if (!string.IsNullOrWhiteSpace(whereSql))
        {
            // 2. 处理用户传入的条件
            var condition = whereSql.Trim();

            // 如果用户习惯性传了 "WHERE Sku = ...", 我们要把它去掉
            if (condition.StartsWith("WHERE", StringComparison.OrdinalIgnoreCase))
            {
                condition = condition[5..]; // 去掉 "WHERE" 字符
            }

            // 3. ★ 关键修正：用 AND 连接，而不是再加一个 WHERE ★
            // 加上括号 () 是为了防止用户传入 OR 条件破坏前面的 IsDeleted 逻辑
            sql += $" AND ({condition})";
        }

        // 4. 执行查询
        var rst = await Connection.QueryAsync<T>(sql, param, transaction: Transaction);
        return rst.AsList(); // 或者 return [.. rst]; (C# 12写法)
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

        var whereClause = ParseQueryCriteria(criteria, parameters);
        sb.Append(whereClause);

        var finalSql = sb.ToString();

        // ★ 仓储层专属：动态 SQL 追踪日志（仅 Debug 级别生效）
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("执行动态查询 [{TableName}]，生成的 SQL: {Sql}", TableName, finalSql);
        }

        var result = await Connection.QueryAsync<T>(finalSql, parameters, transaction: Transaction);
        return [.. result];
    }




    [System.Text.RegularExpressions.GeneratedRegex(@"[^a-zA-Z0-9_,\s\.\[\]]")]
    private static partial System.Text.RegularExpressions.Regex OrderByRegex();
    /// <summary>
    /// 泛型分页查询 (基于 QueryCriteria)
    /// </summary>
    /// <param name="criteria">查询条件封装对象</param>
    /// <param name="pageIndex">页码 (索引从1开始,内部会处理)</param>
    /// <param name="pageSize">每页条数</param>
    /// <param name="orderBy">排序字段 (例如 "ID DESC")，必填，否则分页会报错</param>
    /// <returns>分页结果</returns>
    public virtual async Task<PagedResult<T>> GetPagedListAsync(QueryCriteria criteria, int pageIndex, int pageSize, string orderBy = "Id DESC")
    {
        // 🛡️ 安全墙：清洗 orderBy
        if (string.IsNullOrWhiteSpace(orderBy))
        {
            orderBy = "Id DESC";
        }
        else
        {
            orderBy = OrderByRegex().Replace(orderBy, "");
        }

        // 基础底座
        var sb = new StringBuilder("WHERE IsDeleted = 0"); // 建议用 = 0 替代 <> 1，很多数据库对 = 的索引优化更好
        var parameters = new DynamicParameters();

        // 获取动态条件 (此时 whereClause 类似于 " AND [Code] = @Code_0 AND ([A]=@.. OR [B]=@..)")
        var whereClause = ParseQueryCriteria(criteria, parameters);

        //直接 Append 即可！因为 whereClause 自己已经带了 AND 开头了。
        if (!string.IsNullOrWhiteSpace(whereClause))
        {
            sb.Append(whereClause);
        }

        // 分页计算
        if (pageIndex < 1) pageIndex = 1;
        int offset = (pageIndex - 1) * pageSize;

        parameters.Add("@Offset", offset);
        parameters.Add("@Limit", pageSize);

        var sql = $@"
            SELECT COUNT(1) FROM [{TableName}] WITH(NOLOCK) {sb};
            SELECT * FROM [{TableName}] WITH(NOLOCK) {sb} 
            ORDER BY {orderBy} 
            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

        // ★ 日志追踪
        if (Logger.IsEnabled(LogLevel.Debug))
        {
            Logger.LogDebug("执行分页动态查询 [{TableName}]，生成的 SQL: {Sql}", TableName, sql);
        }

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
    // 增删改 (Write) - 使用 Output 这里的 Id 为 int
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

        // 回写 Id
        var idProp = typeof(T).GetProperty("Id");
        if (idProp != null && idProp.CanWrite) idProp.SetValue(entity, newId);

        return newId;
    }
    public virtual async Task<int> UpdateAsync(T entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        // 拼接 SET 语句
        var setClause = string.Join(", ", _writeableProps.Select(p => $"[{p}] = @{p}"));
        var sql = $"UPDATE [{TableName}] SET {setClause} WHERE [Id] = @Id";

        return await Connection.ExecuteAsync(sql, entity, transaction: Transaction);
    }

    public virtual async Task<int> DeleteSoftAsync(int id)
    {
        var sql = $"UPDATE [{TableName}] SET IsDelete = 1 WHERE [Id] = @Id";
        return await Connection.ExecuteAsync(sql, new { Id = id }, transaction: Transaction);
    }

    public virtual async Task<int> DeleteAsync(int id)
    {
        var sql = $"DELETE FROM [{TableName}] WHERE [Id] = @Id";
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
        var idProperty = typeof(T).GetProperty("Id");
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

        var sql = $"DELETE FROM [{TableName}] WHERE Id IN @IdList";

        // 直接用 Connection 和 Transaction 属性
        return await Connection.ExecuteAsync(sql, new { IdList = idList }, transaction: Transaction);
    }
    public virtual async Task<int> DeleteRangeSoftAsync(IEnumerable<int> idList)
    {
        if (idList == null || !idList.Any()) return 0;

        var sql = $"UPDATE [{TableName}] SET IsDeleted=1 WHERE Id IN @IdList";

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

        var idProp = typeof(T).GetProperty("Id") ?? throw new InvalidOperationException("实体必须包含 ID 属性才能进行批量更新");
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

            var idParamName = $"p{i}_Id";
            parameters.Add(idParamName, idProp.GetValue(entityList[i]));

            sqlBuilder.AppendLine($"UPDATE [{TableName}] SET {string.Join(", ", setClauses)} WHERE [Id] = @{idParamName};");
        }

        // 直接用 Connection 和 Transaction 属性
        return await Connection.ExecuteAsync(sqlBuilder.ToString(), parameters, transaction: Transaction);
    }

    /// <summary>
    /// 辅助方法：生成单个条件的 SQL 片段，并填充参数
    /// </summary>
    private static string BuildConditionSql(string propName, QueryCondition cond, string paramName, DynamicParameters parameters)
    {
        string safeField = $"[{propName.Replace("[", "").Replace("]", "")}]";

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
                // 注意：Dapper 中 IN 后面直接跟参数名即可，例如 IN @Ids，千万不要写成 IN (@Ids)
                parameters.Add(paramName, cond.值1);
                return $"{safeField} IN {paramName}";

            default:
                return "1=1";
        }
    }

    /// <summary>
    /// 解析查询条件对象为 SQL
    /// </summary>
    protected string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters)
    {
        if (criteria == null || (criteria.Items.Count == 0 && criteria.OrGroups.Count == 0))
        {
            return string.Empty;
        }

        StringBuilder sqlBuilder = new();

        // Part 1: AND 条件
        int andIndex = 0;
        foreach (var item in criteria.Items)
        {
            string propName = item.Field;
            var cond = item.Condition;
            string paramName = $"@{propName}_{andIndex}";

            string sqlPart = BuildConditionSql(propName, cond, paramName, parameters);
            // 每次生成都自带 AND 前缀
            sqlBuilder.Append($" AND {sqlPart}");

            andIndex++;
        }

        // Part 2: OR 分组
        int groupIndex = 0;
        foreach (var groupList in criteria.OrGroups)
        {
            if (groupList.Count == 0) continue;

            List<string> orParts = [];
            int orItemIndex = 0;

            foreach (var item in groupList)
            {
                string propName = item.Field;
                var cond = item.Condition;
                string paramName = $"@Or_{groupIndex}_{propName}_{orItemIndex}";

                string sqlPart = BuildConditionSql(propName, cond, paramName, parameters);
                orParts.Add(sqlPart);
                orItemIndex++;
            }

            if (orParts.Count > 0)
            {
                string groupSql = $"({string.Join(" OR ", orParts)})";
                // 整个 OR 组作为一个整体，与外部用 AND 连接
                sqlBuilder.Append($" AND {groupSql}");
            }
            groupIndex++;
        }

        // 清理了刚才在这里的残余错误代码！！！
        return sqlBuilder.ToString();
    }








}
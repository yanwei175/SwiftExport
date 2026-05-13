using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auth;
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
    private readonly IAuditStampService _auditStamp = context.AuditStamp;

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
    public virtual async Task<IReadOnlyList<int>> GetExistingIdListAsync(IEnumerable<int> idList, bool withUpdLock = false)
    {
        if (idList == null || !idList.Any()) return [];
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $"SELECT [Id] FROM [{TableName}] {lockHint} WHERE [Id] IN @Ids";
        var rst = await Connection.QueryAsync<int>(sql, new { Ids = idList }, transaction: Transaction);
        return [.. rst];
    }

    public virtual async Task<T?> GetByIdAsync(int id, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [Id] = @Id AND (IsDeleted = 0 OR IsDeleted IS NULL)";
        return await Connection.QuerySingleOrDefaultAsync<T>(sql, new { Id = id }, transaction: Transaction);
    }

    public virtual async Task<IReadOnlyList<T>> GetByIdListAsync(IEnumerable<int> idList, bool withUpdLock = false)
    {
        if (idList == null || !idList.Any()) return [];
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE [Id] IN @Ids AND (IsDeleted = 0 OR IsDeleted IS NULL)";
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
        var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE IsDeleted = 0 OR IsDeleted IS NULL ORDER BY ISNULL(UpdatedDate, CreatedDate) DESC";
        var rst = await Connection.QueryAsync<T>(sql, transaction: Transaction);
        return [.. rst];
    }
    public virtual async Task<IReadOnlyList<T>> GetListAsync(string whereSql, object? param = null, bool withUpdLock = false)
    {
        // 根据参数决定用 NOLOCK 还是 UPDLOCK
        string lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";

        // 1. 基础 SQL 已经包含了 WHERE (IsDeleted = 0 OR IsDeleted IS NULL)
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL";

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

    public virtual async Task<IReadOnlyList<T>> GetListAsync(object noNameObj, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sql = $"SELECT * FROM [{TableName}] {lockHint} WHERE (IsDeleted = 0 OR IsDeleted IS NULL)";
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

    public virtual async Task<IReadOnlyList<T>> GetListAsync(QueryCriteria criteria, bool withUpdLock = false)
    {
        var lockHint = withUpdLock ? "WITH (UPDLOCK, ROWLOCK)" : "WITH (NOLOCK)";
        var sb = new StringBuilder($"SELECT * FROM [{TableName}] {lockHint} WHERE IsDeleted = 0 OR IsDeleted IS NULL");
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
        var sb = new StringBuilder("WHERE (IsDeleted = 0 OR IsDeleted IS NULL)"); // 建议用 = 0 替代 <> 1，很多数据库对 = 的索引优化更好
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

        // 自动填充审计字段（CreatedBy/CreatedDate/UpdatedBy/UpdatedDate）
        _auditStamp.StampForInsert(entity);
        entity.IsDeleted = false; // 确保新增时 IsDeleted 字段为 false
        if (entity.Version <= 0) entity.Version = 1;
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
        // 自动填充审计字段（UpdatedBy/UpdatedDate 强制刷新）
        _auditStamp.StampForUpdate(entity);

        // 中文注释：乐观并发控制。
        // 1) 使用原 Version 做 WHERE 条件
        // 2) 命中成功后 Version 自增
        var originalVersion = entity.Version <= 0 ? 1 : entity.Version;
        entity.Version = originalVersion + 1;

        // 拼接 SET 语句
        var setClause = string.Join(", ", _writeableProps.Select(p => $"[{p}] = @{p}"));
        var sql = $"UPDATE [{TableName}] SET {setClause} WHERE [Id] = @Id AND [Version] = @OriginalVersion";

        var parameters = new DynamicParameters(entity);
        parameters.Add("OriginalVersion", originalVersion);

        var rows = await Connection.ExecuteAsync(sql, parameters, transaction: Transaction);

        if (rows <= 0)
        {
            // 恢复对象版本，避免调用方拿到错误状态
            entity.Version = originalVersion;
            throw new DBConcurrencyException($"[{TableName}] 更新失败：数据已被其他用户修改（Id={entity.Id}, Version={originalVersion}）。");
        }

        return rows;
    }

    public virtual async Task<int> DeleteSoftAsync(int id)
    {
        var sql = $"UPDATE [{TableName}] SET UpdatedDate = {DateTime.Now}, IsDeleted = 1 WHERE [Id] = @Id";
        return await Connection.ExecuteAsync(sql, new { Id = id}, transaction: Transaction);
    }

    public virtual async Task<int> DeleteAsync(int id, bool softDelete = true)
    {
        string sql;
        if (softDelete)
        {
            sql = $"UPDATE [{TableName}] SET UpdatedDate = {DateTime.Now}, IsDeleted = 1 WHERE [Id] = @Id";
        }
        else
        {
            sql = $"DELETE FROM [{TableName}] WHERE [Id] = @Id";
        }
        return await Connection.ExecuteAsync(sql, new { Id = id}, transaction: Transaction);
    }

    // =============================================================
    // 批量操作 (Batch Write)
    // =============================================================
    public virtual async Task<IReadOnlyList<int>> AddRangeAsync(IEnumerable<T> entities)
    {
        if (entities == null) throw new ArgumentException("实体集合不能为空");
        var entityList = entities.ToList();
        if (entityList.Count == 0) return [];

        //遍历实体设置 UpdatedDate（如果有这个属性的话），确保批量插入时这个字段不为 null
        foreach (var entity in entityList)
        {
            // 自动审计打戳（CreatedBy/CreatedDate/UpdatedBy/UpdatedDate）
            _auditStamp.StampForInsert(entity);            
            entity.IsDeleted = false; // 确保批量插入时 IsDeleted 字段为 false
            if (entity.Version <= 0) entity.Version = 1;
        }
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

    public virtual async Task<int> DeleteRangeAsync(IEnumerable<int> idList, bool softDelete = true)
    {
        if (idList == null || !idList.Any()) return 0;

        string sql;
        if (softDelete)
        {
            sql = $"UPDATE [{TableName}] SET IsDeleted = 1 WHERE Id IN @IdList";
        }
        else
        {
            sql = $"DELETE FROM [{TableName}] WHERE Id IN @IdList";
        }
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
        var entityList = entities.OrderBy(x=>x.Id).ToList();
        if (entityList.Count == 0) return 0;
        //遍历实体设置 UpdatedDate（如果有这个属性的话），确保批量更新时这个字段不为 null
        foreach (var entity in entityList)
        {
            // 自动审计打戳（UpdatedBy/UpdatedDate 强制刷新）
            _auditStamp.StampForUpdate(entity);
        }
        // 1. 准备属性
        var updateProps = _writeableProps
            .Select(name => typeof(T).GetProperty(name))
            .Where(p => p != null)
            .Select(p => p!)
            .ToList();

        var idProp = typeof(T).GetProperty("Id") ?? throw new InvalidOperationException("实体必须包含 ID 属性才能进行批量更新");
        var parameters = new DynamicParameters();
        var sqlBuilder = new StringBuilder();
        var originalVersionMap = new Dictionary<int, int>();

        for (int i = 0; i < entityList.Count; i++)
        {
            var setClauses = new List<string>();
            var originalVersion = entityList[i].Version <= 0 ? 1 : entityList[i].Version;
            entityList[i].Version = originalVersion + 1;
            originalVersionMap[entityList[i].Id] = originalVersion;

            foreach (var prop in updateProps)
            {
                var paramName = $"p{i}_{prop.Name}";
                setClauses.Add($"[{prop.Name}] = @{paramName}");
                parameters.Add(paramName, prop.GetValue(entityList[i]));
            }

            var idParamName = $"p{i}_Id";
            parameters.Add(idParamName, idProp.GetValue(entityList[i]));
            var verParamName = $"p{i}_OriginalVersion";
            parameters.Add(verParamName, originalVersion);

            sqlBuilder.AppendLine($"UPDATE [{TableName}] SET {string.Join(", ", setClauses)} OUTPUT INSERTED.[Id] WHERE [Id] = @{idParamName} AND [Version] = @{verParamName};");
        }

        // 直接用 Connection 和 Transaction 属性
        var updatedIds = (await Connection.QueryAsync<int>(sqlBuilder.ToString(), parameters, transaction: Transaction)).Distinct().ToList();

        // 只有当更新成功的 Id 数量小于传入实体数时，才说明存在并发冲突
        if (updatedIds.Count < entityList.Count)
        {
            var expectedIds = entityList.Select(x => x.Id).Distinct().ToList();
            var conflictIds = expectedIds.Except(updatedIds).ToList();

            // 回滚内存中的 Version，避免调用方拿到错误版本号
            foreach (var entity in entityList)
            {
                if (conflictIds.Contains(entity.Id) && originalVersionMap.TryGetValue(entity.Id, out var ov))
                {
                    entity.Version = ov;
                }
            }

            throw new DBConcurrencyException($"并发操作冲突：以下 Id 更新失败（可能被他人修改）=> {string.Join(",", conflictIds)}");
        }

        return updatedIds.Count;
    }

    /// <summary>
    /// 辅助方法：生成单个条件的 SQL 片段。支持 "t.FieldName" 或 "FieldName" 格式
    /// </summary>
    private static string BuildConditionSql(string propName, QueryCondition cond, string paramName, DynamicParameters parameters)
    {
        // 核心修复：处理带别名的字段，例如 t.CategoryId -> t.[CategoryId]
        string safeField;
        if (propName.Contains('.'))
        {
            var parts = propName.Split('.');
            // parts[0] 是别名 (t), parts[1] 是字段名
            string field = parts[1].Replace("[", "").Replace("]", "");
            safeField = $"{parts[0]}.[{field}]";
        }
        else
        {
            safeField = $"[{propName.Replace("[", "").Replace("]", "")}]";
        }

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
                // Dapper 处理 IN 子句：对于集合类型，需要显式包含在圆括号内 IN (@ParamName)
                // 在参数字典中，保存真正的参数名（不带@），由于Dapper的特定版本处理 List 时替换，圆括号是明确要求的标准写法
                //parameters.Add(paramName.TrimStart('@'), cond.值1);
                //return $"{safeField} IN (@{paramName.TrimStart('@')})";

                parameters.Add(paramName, cond.值1);
                return $"{safeField} IN {paramName}";

            default:
                return "1=1";
        }
    }

    /// <summary>
    /// 解析查询条件对象为 SQL
    /// </summary>
    protected string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters, string prefix = "")
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
            // 拼接前缀：如果不为空且不含点，则加上前缀
            string fieldName = (string.IsNullOrEmpty(prefix) || item.Field.Contains('.'))
                               ? item.Field
                               : $"{prefix}{item.Field}";
            // 参数名不能有点，将点替换为下划线
            string paramName = $"@{fieldName.Replace(".", "_")}_A{andIndex}";

            string sqlPart = BuildConditionSql(fieldName, item.Condition, paramName, parameters);
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
                string fieldName = (string.IsNullOrEmpty(prefix) || item.Field.Contains('.'))
                                   ? item.Field
                                   : $"{prefix}{item.Field}";

                string paramName = $"@Or_{groupIndex}_{fieldName.Replace(".", "_")}_{orItemIndex}";
                orParts.Add(BuildConditionSql(fieldName, item.Condition, paramName, parameters));
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

        // 返回拼接后的 WHERE 条件片段（不包含 WHERE 关键字）
        return sqlBuilder.ToString();
    }


}
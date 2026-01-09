using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace SwiftExport.Infrastructure.Repositories
{
    /// <summary>
    /// 精简版通用 Dapper 仓储基类（只保留单条记录操作）
    /// 约定：
    /// - BaseEntity 的字段（ID, IsDelete, CreateDate, ChangeTime）由数据库维护，插入/更新时排除这些字段
    /// - 插入后回写自增 ID（SQL Server: SCOPE_IDENTITY()）
    /// - Expression 参数目前仅支持 null（复杂条件请传入自定义 SQL 或扩展器）
    /// </summary>
    public class DapperRepository<T> : IRepository<T> where T : BaseEntity
    {
        protected readonly IMappingCache _cache;// 供子类使用
        protected readonly IDbConnectionFactory _connFac;// 供子类使用
        protected readonly string tableName;// 供子类使用


        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connFactory">
        /// dbconnection 的工厂
        /// </param>
        /// <param name="cache">
        /// MappingCache映射
        /// </param>
        public DapperRepository(IMappingCache cache, IDbConnectionFactory connectionFactory)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            tableName = _cache.GetTableNameByEntity<T>() ?? typeof(T).Name;
            _connFac = connectionFactory;
        }
        /// <summary>
        /// 私有方法,获取映射的列名（排除基础字段）主要用于插入和更新操作,数据库里自动处理的字段需要排除.
        /// </summary>
        private Dictionary<PropertyInfo, string> GetMappedPropertyColumns<T2>(IMappingCache cache, bool 排除基础字段 = true,
            IEnumerable<string> 排除扩展字段 = null) where T2 : BaseEntity
        {
            var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "ID", "IsDelete", "CreateDate", "ChangeTime"
            };

            if (排除扩展字段 != null)
            {
                foreach (var field in 排除扩展字段)
                    excluded.Add(field);
            }

            var props = typeof(T2).GetProperties(BindingFlags.Public | BindingFlags.Instance)
                                 .Where(p => p.CanRead && p.CanWrite);

            if (排除基础字段)
                props = props.Where(p => !excluded.Contains(p.Name));

            var dict = new Dictionary<PropertyInfo, string>();
            foreach (var prop in props)
            {
                var columnName = cache.GetFieldNameByEntityProperty<T2>(prop.Name) ?? prop.Name;
                dict[prop] = columnName;
            }

            return dict;
        }

        public async Task<IReadOnlyList<T>> GetAllAsync()
        {
            var sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1";
            using (var _connection = _connFac.CreateConnection())
                return (await _connection.QueryAsync<T>(sql)).ToList();
        }

        public async Task<bool> ExistsIdAsync(int id)
        {
            var tableName = _cache.GetTableNameByEntity<T>() ?? typeof(T).Name;
            var sql = $"SELECT Count(ID) FROM [{tableName}] WITH (NOLOCK) WHERE [ID] = @ID";
            using (var _connection = _connFac.CreateConnection())
            {
                var count = await _connection.ExecuteScalarAsync<int>(sql, new { ID = id });
                return count > 0;
            }
        }

        public async Task<T> GetSingleEntityAsync()
        {
            var sql = $"SELECT * FROM [{tableName}] WITH (NOLOCK)";
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryFirstAsync<T>(sql);
        }

        public async Task<T> GetByIdAsync(int id)
        {
            var sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1 AND [ID] = @ID";
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QuerySingleOrDefaultAsync<T>(sql, new { ID = id });
        }

        public async Task<IReadOnlyList<T>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var sql = $"SELECT * FROM  [{tableName}] Where IsDelete<>1 AND [ID] IN @ids";
            using (var _connection = _connFac.CreateConnection())
                return (await _connection.QueryAsync<T>(sql, new { ids })).ToList();
        }

        public async Task<int> AddAsync(T entity, IUnitOfWork uow)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var mappedProps = GetMappedPropertyColumns<T>(_cache); // 排除基础字段

            var columns = string.Join(", ", mappedProps.Values.Select(c => $"[{c}]"));
            var parameters = string.Join(", ", mappedProps.Keys.Select(p => $"@{p.Name}"));

            var sql = $@"INSERT INTO [{tableName}] ({columns}) VALUES ({parameters});
                        SELECT CAST(SCOPE_IDENTITY() AS INT);";
            var newId = await uow.Connection.ExecuteScalarAsync<int>(
                                    sql,
                                    entity,
                                    transaction: uow.Transaction
                                );

            var idProperty = typeof(T).GetProperty("ID");
            if (idProperty != null && idProperty.PropertyType == typeof(int))
            {
                idProperty.SetValue(entity, newId);
            }

            return newId;

        }

        public async Task<int> UpdateAsync(T entity, IUnitOfWork uow)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var mappedProps = GetMappedPropertyColumns<T>(_cache);
            var setClause = string.Join(", ", mappedProps.Select(kv => $"[{kv.Value}] = @{kv.Key.Name}"));
            var sql = $"UPDATE [{tableName}] SET {setClause} WHERE [ID] = @ID";

            return await uow.Connection.ExecuteAsync(sql, entity, transaction: uow.Transaction);
        }

        public async Task<int> DeleteAsync(int id, IUnitOfWork uow)
        {
            var sql = $"DELETE FROM [{tableName}] WHERE [ID] = @ID";
            return await uow.Connection.ExecuteAsync(sql, new { ID = id }, transaction: uow.Transaction);
        }

        public async Task<int> DeleteSoftAsync(int id, IUnitOfWork uow)
        {
            var sql = $"UPDATE [{tableName}] SET IsDelete=1 WHERE [ID] = @ID";
            return await uow.Connection.ExecuteAsync(sql, new { ID = id }, transaction: uow.Transaction);
        }

        public async Task<IReadOnlyList<int>> AddRangeAsync(IEnumerable<T> entities, IUnitOfWork uow)
        {
            if (entities == null) throw new ArgumentException("实体集合不能为空");
            var entityList = entities.ToList();
            if (!entityList.Any()) throw new ArgumentException("实体集合不能为空");

            var mappedProps = GetMappedPropertyColumns<T>(_cache);
            if (mappedProps == null || mappedProps.Count == 0)
                throw new InvalidOperationException("没有可插入的映射属性。");

            var columns = string.Join(", ", mappedProps.Values.Select(c => $"[{c}]"));

            // 为每一行生成独立的参数名，避免把 IEnumerable 直接作为参数传入 Dapper
            var valueRows = new List<string>();
            var parameters = new DynamicParameters();

            for (int i = 0; i < entityList.Count; i++)
            {
                var rowParams = new List<string>();
                foreach (var prop in mappedProps.Keys)
                {
                    var paramName = $"p{i}_{prop.Name}";
                    rowParams.Add($"@{paramName}");
                    var value = prop.GetValue(entityList[i]);
                    parameters.Add(paramName, value);
                }
                valueRows.Add($"({string.Join(", ", rowParams)})");
            }

            var sql = $@"
                INSERT INTO [{tableName}] ({columns})
                OUTPUT INSERTED.ID
                VALUES {string.Join(", ", valueRows)};
            ";

            var newIds = (await uow.Connection.QueryAsync<int>(
                                sql,
                                parameters,
                                transaction: uow.Transaction)).ToList();

            // 回写 ID 到实体（如果有 ID 属性）
            var idProperty = typeof(T).GetProperty("ID");
            if (idProperty != null && idProperty.PropertyType == typeof(int))
            {
                for (int i = 0; i < newIds.Count && i < entityList.Count; i++)
                {
                    idProperty.SetValue(entityList[i], newIds[i]);
                }
            }

            return newIds;
        }

        public async Task<int> DeleteRangeByIdsAsync(IEnumerable<int> ids, IUnitOfWork uow)
        {
            if (ids == null || !ids.Any()) return 0;

            var sql = $"DELETE FROM [{tableName}] WHERE ID IN @Ids";

            return await uow.Connection.ExecuteAsync(sql, new { Ids = ids }, transaction: uow.Transaction);
        }

        public async Task<int> UpdateRangeByIdsAsync(IEnumerable<T> entities, IUnitOfWork uow)
        {
            if (entities == null) throw new ArgumentException("实体集合不能为空");
            var entityList = entities.ToList();
            if (!entityList.Any()) return 0;

            var mappedProps = GetMappedPropertyColumns<T>(_cache);
            // 过滤掉主键 ID 属性，因为主键通常不出现在 SET 子句中
            var updateProps = mappedProps.Keys.Where(p => p.Name != "ID").ToList();

            var parameters = new DynamicParameters();
            var sqlBuilder = new StringBuilder();
            for (int i = 0; i < entityList.Count; i++)
            {
                var setClauses = new List<string>();
                foreach (var prop in updateProps)
                {
                    var paramName = $"p{i}_{prop.Name}";
                    setClauses.Add($"[{mappedProps[prop]}] = @{paramName}");
                    parameters.Add(paramName, prop.GetValue(entityList[i]));
                }

                // 必须为主键 ID 单独加参数，用于 WHERE 子句
                var idParamName = $"p{i}_ID";
                var idValue = typeof(T).GetProperty("ID")?.GetValue(entityList[i]);
                parameters.Add(idParamName, idValue);

                sqlBuilder.AppendLine($"UPDATE [{tableName}] SET {string.Join(", ", setClauses)} WHERE [ID] = @{idParamName};");
            }

            return await uow.Connection.ExecuteAsync(sqlBuilder.ToString(), parameters, transaction: uow.Transaction);
        }



        public async Task<IReadOnlyList<int>> GetExistingIdsAsync(IEnumerable<int> ids)
        {
            using (var connection = _connFac.CreateConnection())
            {
                // SQL 修正：仅查询 CustomerCode 字段，并使用 IN 语句
                var sql = $"SELECT ID FROM [{tableName}] WHERE ID IN @IDs";

                // Dapper 优势：QueryAsync 自动将 IEnumerable<string> 映射到 IN 语句
                return (await connection.QueryAsync<int>(
                    sql,
                    new { IDs = ids } // 将参数名与 SQL 中的 @Codes 匹配
                )).ToList();
            }
        }


        /// <summary>
        /// 自定义查询 (基于 QueryCriteria)
        /// </summary>
        /// <param name="criteria">查询条件封装对象</param>
        /// <returns>对象集合</returns>
        public async Task<IReadOnlyList<T>> GetByUserWhereAsync(QueryCriteria criteria)
        {
            StringBuilder sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] WHERE 1=1");

            DynamicParameters parameters = new DynamicParameters();

            // 3. ★ 调用解析器，把对象转换成 SQL
            // 解析器会把生成的 SQL 拼回来，并把参数塞进 parameters 里
            string whereClause = ParseQueryCriteria(criteria, parameters);

            // 4. 将解析出来的 WHERE 子句拼接到主 SQL 后面
            sqlBuilder.Append(whereClause);

            // 5. 执行查询
            string sql = sqlBuilder.ToString();

            using (var _connection = _connFac.CreateConnection())
            {
                return (await _connection.QueryAsync<T>(sql, parameters)).ToList();
            }
        }

        /// <summary>
        /// 泛型分页查询 (基于 QueryCriteria)
        /// </summary>
        /// <param name="criteria">查询条件封装对象</param>
        /// <param name="pageIndex">页码 (索引从1开始,内部会处理)</param>
        /// <param name="pageSize">每页条数</param>
        /// <param name="orderBy">排序字段 (例如 "ID DESC")，必填，否则分页会报错</param>
        /// <returns>分页结果</returns>
        public async Task<PagedResult<T>> GetPagedListAsync(
            QueryCriteria criteria,
            int pageIndex,
            int pageSize,
            string orderBy = "ID DESC")
        {
            // 1. 初始化基础 SQL (预埋 IsDelete 条件)
            StringBuilder sqlBuilder = new StringBuilder($"WHERE 1 = 1");
            DynamicParameters parameters = new DynamicParameters();

            // 2. 复用解析逻辑 (调用刚才定义的 ParseQueryCriteria)
            // 这会将 criteria 里的条件解析为 SQL 并追加到 parameters 中
            string dynamicCondition = ParseQueryCriteria(criteria, parameters);
            sqlBuilder.Append(dynamicCondition);

            // 3. 计算分页参数 (索引1转0)
            if (pageIndex < 1) pageIndex = 1;
            int offset = (pageIndex - 1) * pageSize;

            parameters.Add("@Offset", offset);
            parameters.Add("@Limit", pageSize);

            // 4. 组装最终 SQL
            // 技巧：使用 QueryMultiple 一次性执行两条 SQL，减少网络往返
            string whereSql = sqlBuilder.ToString();

            string sql = $@"
        -- 语句1: 查总数
        SELECT COUNT(1) FROM [{tableName}] {whereSql};
        
        -- 语句2: 查分页数据
        SELECT * FROM [{tableName}] {whereSql} 
        ORDER BY {orderBy} 
        OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

            // 5. 执行查询
            using (var conn = _connFac.CreateConnection())
            {
                using (var multi = await conn.QueryMultipleAsync(sql, parameters))
                {
                    // 按顺序读取结果
                    var total = await multi.ReadFirstAsync<int>();
                    var items = await multi.ReadAsync<T>();

                    return new PagedResult<T>
                    {
                        Items = items.ToList(),
                        TotalCount = total,
                        PageIndex = pageIndex,
                        PageSize = pageSize
                        // TotalPages 属性会在 PagedResult 内部自动计算
                    };
                }
            }
        }















        /// <summary>
        /// 辅助方法：生成单个条件的 SQL 片段，并填充参数
        /// </summary>
        private string BuildConditionSql(string propName, QueryCondition cond, string paramName, DynamicParameters parameters)
        {
            // 1. 获取安全的数据库列名 (保留你原有的缓存逻辑)
            string field = _cache.GetFieldNameByEntityProperty<T>(propName) ?? propName;
            string safeField = $"[{field}]";

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

                case 查询操作符.包含某值: // 如果你有 IN 的话，这里扩展
                                 // 需要特殊处理 IN 的参数，通常 Dapper 支持传入数组
                    parameters.Add(paramName, cond.值1);
                    return $"{safeField} IN {paramName}";

                default:
                    return "1=1"; // 容错
            }
        }


        protected string ParseQueryCriteria(QueryCriteria criteria, DynamicParameters parameters)
        {
            // 基本校验
            if (criteria == null || (criteria.Items.Count == 0 && criteria.OrGroups.Count == 0))
            {
                return string.Empty;
            }

            StringBuilder sqlBuilder = new StringBuilder();

            // =======================================================
            // Part 1: 处理固定 AND 条件 (criteria.Items)
            // =======================================================
            foreach (var kvp in criteria.Items)
            {
                string propName = kvp.Key;
                var cond = kvp.Value;
                string paramName = $"@{propName}"; // AND 条件直接用属性名做参数

                // 调用抽取的方法
                string sqlPart = BuildConditionSql(propName, cond, paramName, parameters);

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

                List<string> orParts = new List<string>();

                foreach (var kvp in groupDict)
                {
                    string propName = kvp.Key;
                    var cond = kvp.Value;

                    // ★ 关键：OR 参数名必须唯一化，避免与 Part 1 或其他组冲突
                    // 例如: @Or_0_SKU, @Or_0_Description
                    string paramName = $"@Or_{groupIndex}_{propName}";

                    // 调用抽取的方法
                    string sqlPart = BuildConditionSql(propName, cond, paramName, parameters);

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
}

using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;
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
        public DapperRepository( IMappingCache cache, IDbConnectionFactory connectionFactory)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            tableName = _cache.GetTableNameByEntity<T>() ?? typeof(T).Name;
            _connFac = connectionFactory;
        }
        /// <summary>
        /// 私有方法,获取映射的列名（排除基础字段）主要用于插入和更新操作,需要排除的字段是在数据库里自动生成的.
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

        public async Task<IEnumerable<T>> GetAllAsync()
        {
            var sql = $"SELECT * FROM [{tableName}] WHERE IsDelete<>1";
            using (var _connection =  _connFac.CreateConnection())
            return await _connection.QueryAsync<T>(sql);
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

        public async Task<IEnumerable<T>> GetByIdsAsync(IEnumerable<int> ids)
        {
            var sql = $"SELECT * FROM  [{tableName}] Where IsDelete<>1 AND [ID] IN @ids";
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryAsync<T>(sql, new { ids });
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

        public async Task<IEnumerable<int>> AddRangeAsync(IEnumerable<T> entities, IUnitOfWork uow)
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
            if (ids == null || !ids.Any())
                throw new ArgumentException("ID集合不能为空");

            var sql = $"DELETE FROM [{tableName}] WHERE ID IN @Ids";

                return await uow.Connection.ExecuteAsync(sql, new {Ids = ids}, transaction: uow.Transaction);
        }

        public async Task<int> UpdateRangeByIdsAsync(IEnumerable<T> entities, IUnitOfWork uow)
        {
            if (entities == null || !entities.Any())
                throw new ArgumentException("实体集合不能为空");

            var mappedProps = GetMappedPropertyColumns<T>(_cache);

            var setClause = string.Join(", ", mappedProps.Keys.Select(p => $"[{mappedProps[p]}] = @{p.Name}"));
            var sql = $"UPDATE [{tableName}] SET {setClause} WHERE ID = @ID";

            return await uow.Connection.ExecuteAsync(sql, entities, transaction: uow.Transaction);
        }

        public async Task<IEnumerable<T>> GetByUserWhereAsync(Dictionary<string, QueryCondition> 条件字典)
        {
                StringBuilder sqlBuilder = new StringBuilder($"SELECT * FROM [{tableName}] WHERE IsDelete<>1");
                DynamicParameters parameters = new DynamicParameters();

                foreach (var kvp in 条件字典)
                {
                string propName = kvp.Key; // 属性名
                                           // 通过映射缓存获取安全的列名
                string field = _cache.GetFieldNameByEntityProperty<T>(propName) ?? propName;

                // 验证字段名：如果 field 是一个不安全的输入，且不是你预期的列名，这里可以抛出异常。
                // 假设 field 是一个安全的列名，我们用方括号包裹它来避免SQL语法冲突
                string safeField = $"[{field}]";
                    var cond = kvp.Value;

                    switch (cond.操作符)
                    {
                        case 查询操作符.等于:
                            sqlBuilder.Append($" AND {safeField} = @{propName}");
                            parameters.Add($"@{propName}", cond.值1);
                            break;

                        case 查询操作符.模糊匹配:
                            sqlBuilder.Append($" AND {safeField} LIKE @{propName}");
                            parameters.Add($"@{propName}", $"%{cond.值1}%");
                            break;

                        case 查询操作符.大于:
                            sqlBuilder.Append($" AND {safeField} > @{propName}");
                            parameters.Add($"@{propName}", cond.值1);
                            break;

                        case 查询操作符.小于:
                            sqlBuilder.Append($" AND {safeField} < @{propName}");
                            parameters.Add($"@{propName}", cond.值1);
                            break;

                        case 查询操作符.大于等于:
                            sqlBuilder.Append($" AND {safeField} >= @{propName}");
                            parameters.Add($"@{propName}", cond.值1);
                            break;

                        case 查询操作符.小于等于:
                            sqlBuilder.Append($" AND {safeField} <= @{propName}");
                            parameters.Add($"@{propName}", cond.值1);
                            break;

                        case 查询操作符.区间:
                            sqlBuilder.Append($" AND {safeField} BETWEEN @{propName}_1 AND @{propName}_2");
                            parameters.Add($"@{propName}_1", cond.值1);
                            parameters.Add($"@{propName}_2", cond.值2);
                            break;
                    }
                }

                string sql = sqlBuilder.ToString();
            using (var _connection = _connFac.CreateConnection())
                return await _connection.QueryAsync<T>(sql, parameters);
        }

        public async Task<IEnumerable<int>> GetExistingIdsAsync(IEnumerable<int> ids)
        {
            using (var connection = _connFac.CreateConnection())
            {
                // SQL 修正：仅查询 CustomerCode 字段，并使用 IN 语句
                var sql = $"SELECT ID FROM [{tableName}] WHERE ID IN @IDs";

                // Dapper 优势：QueryAsync 自动将 IEnumerable<string> 映射到 IN 语句
                return await connection.QueryAsync<int>(
                    sql,
                    new { IDs = ids } // 将参数名与 SQL 中的 @Codes 匹配
                );
            }
        }


    }
}

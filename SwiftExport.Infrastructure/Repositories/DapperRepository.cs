using Dapper;
using SwiftExport.Core.Common;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Interfaces;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using static Dapper.SqlMapper;

namespace SwiftExport.Infrastructure.Repositories
{
    /// <summary>
    /// 精简版通用 Dapper 仓储基类（只保留单条记录操作）
    /// 约定：
    /// - 使用短连接：通过注入的 IDbConnectionFactory 每次方法内 CreateConnection/Open/Dispose
    /// - BaseEntity 的字段（ID, IsDelete, CreateDate, ChangeTime）由数据库维护，插入/更新时排除这些字段
    /// - 插入后回写自增 ID（SQL Server: SCOPE_IDENTITY()）
    /// - Expression 参数目前仅支持 null（复杂条件请传入自定义 SQL 或扩展器）
    /// </summary>
    public class DapperRepository<T> : IRepository<T> where T : BaseEntity
    {
        private readonly IDbConnectionFactory _connFactory;
        private readonly IMappingCache _cache;
        private readonly string tableName;
        /// <summary>
        /// 构造函数
        /// </summary>
        /// <param name="connFactory">
        /// dbconnection 的工厂
        /// </param>
        /// <param name="cache">
        /// MappingCache映射
        /// </param>
        public DapperRepository(IDbConnectionFactory connFactory, IMappingCache cache)
        {
            _connFactory = connFactory ?? throw new ArgumentNullException(nameof(connFactory));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            tableName = _cache.GetTableNameByEntity<T>() ?? typeof(T).Name;
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
            var sql = $"SELECT * FROM [{tableName}] WITH (NOLOCK) WHERE IsDelete<>1";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                var rows = await conn.QueryAsync<T>(sql);
                return rows;
            }
        }

        public async Task<T> GetSingleByIdAsync(int id)
        {
            var sql = $"SELECT * FROM [{tableName}] WITH (NOLOCK) WHERE IsDelete<>1 AND [ID] = @ID";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.QuerySingleOrDefaultAsync<T>(sql, new { ID = id });
            }
        }

        public async Task<int> AddSingleReturnNewIDAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var mappedProps = GetMappedPropertyColumns<T>(_cache); // 排除基础字段

            var columns = string.Join(", ", mappedProps.Values.Select(c => $"[{c}]"));
            var parameters = string.Join(", ", mappedProps.Keys.Select(p => $"@{p.Name}"));

            var sql = $"INSERT INTO [{tableName}] ({columns}) VALUES ({parameters}); SELECT CAST(SCOPE_IDENTITY() AS int);";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                var newId = await conn.QuerySingleAsync<int>(sql, entity);

                var idProp = typeof(T).GetProperty("ID", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (idProp != null && idProp.CanWrite)
                {
                    idProp.SetValue(entity, Convert.ChangeType(newId, idProp.PropertyType));
                }

                return newId;
            }
           
        }

        public async Task<int> UpdateSingleAsync(T entity)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));

            var mappedProps = GetMappedPropertyColumns<T>(_cache);

            var setClause = string.Join(", ", mappedProps.Select(kv => $"[{kv.Value}] = @{kv.Key.Name}"));

            var sql = $"UPDATE [{tableName}] SET {setClause} WHERE [ID] = @ID";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, entity);
            }
        }

        public async Task<int> DeleteSingleByIdAsync(int id)
        {
            var sql = $"DELETE FROM [{tableName}] WHERE [ID] = @ID";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, new { ID = id });
            }
        }

        public async Task<int> DisableSingleByID(int id)
        {
            var sql = $"UPDATE [{tableName}] SET IsDelete=1 WHERE [ID] = @ID";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, new { ID = id });
            }
        }

        public async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities)
        {
            if (entities == null || !entities.Any())
                throw new ArgumentException("实体集合不能为空");

            var mappedProps = GetMappedPropertyColumns<T>(_cache);

            var columns = string.Join(", ", mappedProps.Values.Select(c => $"[{c}]"));
            var parameters = string.Join(", ", mappedProps.Keys.Select(p => $"@{p.Name}"));

            var sql = $"INSERT INTO [{tableName}] ({columns}) VALUES ({parameters}); SELECT CAST(SCOPE_IDENTITY() AS int);";

            using (var conn = _connFactory.CreateConnection())
            {            
                conn.Open();
                var result = new List<T>();
                foreach (var entity in entities)
                {
                    var newId = await conn.QuerySingleAsync<int>(sql, entity);
                    var idProp = typeof(T).GetProperty("ID", BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (idProp != null && idProp.CanWrite)
                    {
                        idProp.SetValue(entity, Convert.ChangeType(newId, idProp.PropertyType));
                    }
                    result.Add(entity);
                }
                return result;
            };
        }

        public async Task<int> DeleteRangeByIdsAsync(IEnumerable<int> ids)
        {
            if (ids == null || !ids.Any())
                throw new ArgumentException("ID集合不能为空");

            var sql = $"DELETE FROM [{tableName}] WHERE ID IN @Ids";

            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, new { Ids = ids });
            };
        }

        public async Task<int> UpdateRangeAsync(IEnumerable<T> entities)
        {
            if (entities == null || !entities.Any())
                throw new ArgumentException("实体集合不能为空");

            var mappedProps = GetMappedPropertyColumns<T>(_cache);

            var setClause = string.Join(", ", mappedProps.Keys.Select(p => $"[{mappedProps[p]}] = @{p.Name}"));
            var sql = $"UPDATE [{tableName}] SET {setClause} WHERE ID = @ID";

            using (var conn = _connFactory.CreateConnection()) 
            {
                conn.Open();
                return await conn.ExecuteAsync(sql, entities);
            };
        }

        public async Task<bool> ExistsIdAsync(int id)
        {
            var tableName = _cache.GetTableNameByEntity<T>() ?? typeof(T).Name;
            var sql = $"SELECT Count(ID) FROM [{tableName}] WITH (NOLOCK) WHERE [ID] = @ID";
            using (var conn = _connFactory.CreateConnection())
            {
                conn.Open();
                var count = await conn.ExecuteScalarAsync<int>(sql, new { ID = id });
                return count > 0;
            }
        }
    }
}

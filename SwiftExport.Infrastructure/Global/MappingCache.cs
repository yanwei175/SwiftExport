using Dapper;
using SwiftExport.Core.Common;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using SwiftExport.Core.Entities;
using SwiftExport.Infrastructure.Interfaces;

namespace SwiftExport.Infrastructure.Global
{
    /// <summary>
    /// 基础设施层的缓存实现，负责从数据库加载表/字段/唯一索引映射
    /// </summary>
    public class MappingCache : IMappingCache
    {
        public Dictionary<string, string> TableMappings { get; private set; }
        public Dictionary<(string 实体名, string 属性名), string> FieldMappings { get; private set; }
        public Dictionary<string, string> UniqueColumnMappings { get; private set; }
        public Dictionary<(string 实体名, string 属性名), string> UIColMappings { get; private set; }

        public MappingCache(IDbConnectionFactory factory)
        {
            LoadMappings(factory);
        }
        private void LoadMappings(IDbConnectionFactory factory)
        {
            using(var connection = factory.CreateConnection())
            {
            // 加载表映射 + 唯一索引
            var tableRecords = connection.Query<(string EntityName, string TableName, string UQName)>(
                "SELECT EntityName, TableName, UQName FROM MappingTables"
            )?.ToList();
            //从实体名拿数据库表名
            TableMappings = (tableRecords != null && tableRecords.Any())
                ? tableRecords.ToDictionary(x => x.EntityName, x => x.TableName)
                : new Dictionary<string, string>();

            //从实体名拿数据库唯一索引列名
            UniqueColumnMappings = (tableRecords != null && tableRecords.Any())
                ? tableRecords
                    .Where(x => !string.IsNullOrWhiteSpace(x.UQName))
                    .ToDictionary(x => x.EntityName, x => x.UQName)
                : new Dictionary<string, string>();

            // 加载字段映射（包括 UI 列名）
            var fieldRecords = connection.Query<(string EntityName, string PropertyName, string ColumnName, string UIColName)>(
                "SELECT EntityName, PropertyName, ColumnName, UIColName FROM MappingFields"
            )?.ToList();

            //从实体名+属性名拿数据库列名
            FieldMappings = (fieldRecords != null && fieldRecords.Any())
                ? fieldRecords.ToDictionary(x => (x.EntityName, x.PropertyName), x => x.ColumnName)
                : new Dictionary<(string, string), string>();

            //从实体名+属性名拿UI列名
            UIColMappings = (fieldRecords != null && fieldRecords.Any())
                ? fieldRecords
                    .Where(x => !string.IsNullOrWhiteSpace(x.UIColName))
                    .ToDictionary(x => (x.EntityName, x.PropertyName), x => x.UIColName)
                : new Dictionary<(string, string), string>();

            }
        }

        public string GetTableNameByEntity<T>() where T : BaseEntity
        {
            var entityName = typeof(T).Name;
            if (string.IsNullOrWhiteSpace(entityName)) return null;

            return TableMappings.TryGetValue(entityName, out var tableName)
                ? tableName
                : entityName; // 没找到就返回类名
        }

        /// <summary>
        /// 获取有唯一索引的字段，如果没有则返回 ID
        /// </summary>
        public string GetUniqueFieldByEntity<T>() where T : BaseEntity
        {
            var entityName = typeof(T).Name;

            if (UniqueColumnMappings != null && UniqueColumnMappings.TryGetValue(entityName, out var uqProperty))
            {
                return GetFieldNameByEntityProperty<T>(uqProperty);
            }

            // 默认返回 ID
            return GetFieldNameByEntityProperty<T>("ID");
        }

        public string GetFieldNameByEntityProperty<T>(string propertyName) where T : BaseEntity
        {
            if (string.IsNullOrWhiteSpace(propertyName)) return null;

            var key = (typeof(T).Name, propertyName);
            return FieldMappings.TryGetValue(key, out var columnName)
                ? columnName
                : propertyName; // 没找到就返回属性名
        }

        public string GetUiColNameByEntityProperty<T>(string propertyName) where T : BaseEntity
        {
            var key = (typeof(T).Name, propertyName);
            return UIColMappings.TryGetValue(key, out var uiColName)
                ? uiColName
                : propertyName; // 没找到就返回属性名
        }




    }
}

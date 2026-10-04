using System.Collections.Concurrent;
using SwiftExport.Core.Common;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>在共享持久化属性集上投影PostgreSQL名称，不改变SQL Server Table/Column映射。</summary>
public static class PostgreSqlEntityMetadata
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    /// <summary>取得PostgreSQL物理映射；参数名和查询别名仍是CLR属性名。</summary>
    public static EntityMetadata GetMetadata(Type entity) => Cache.GetOrAdd(entity, Build);

    /// <summary>构建public模式下的独立映射，拒绝命名转换后冲突的属性。</summary>
    private static EntityMetadata Build(Type entity)
    {
        var source = EntityMetadataCache.GetMetadata(entity);
        var columns = source.Columns.Select(column => new EntityColumnMetadata(column.Property, PostgreSqlNames.FromClr(column.PropertyName))).ToArray();
        if (columns.Select(column => column.ColumnName).Distinct(StringComparer.Ordinal).Count() != columns.Length)
            throw new InvalidOperationException($"实体{entity.Name}的PostgreSQL列名发生碰撞。");
        return new EntityMetadata
        {
            EntityType = entity, SchemaName = "public", TableName = PostgreSqlNames.FromClr(entity.Name),
            Columns = Array.AsReadOnly(columns), KeyName = columns.Single(c => c.PropertyName == nameof(BaseEntity.Uid)).ColumnName,
            AllColumns = columns.Select(c => c.ColumnName).ToArray(), InsertColumns = columns.Select(c => c.ColumnName).ToArray(),
            UpdateColumns = columns.Where(c => c.PropertyName is not nameof(BaseEntity.Uid) and not nameof(BaseEntity.CreatedBy) and not nameof(BaseEntity.CreatedDate)).Select(c => c.ColumnName).ToArray(),
            HasVersion = source.HasVersion, HasSoftDelete = source.HasSoftDelete
        };
    }
}

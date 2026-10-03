using SwiftExport.Core.Common;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>为建表与通用仓储缓存同一份实体映射，按基类到派生类保持稳定声明顺序。</summary>
public static class EntityMetadataCache
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    /// <summary>读取实体的缓存映射。</summary>
    public static EntityMetadata GetMetadata<T>() => GetMetadata(typeof(T));

    /// <summary>构建并缓存持久化属性，拒绝歧义映射。</summary>
    public static EntityMetadata GetMetadata(Type type) => Cache.GetOrAdd(type, BuildMetadata);

    /// <summary>取得未加方言引号的表名，兼容现有专用仓储。</summary>
    public static string GetTableName<T>() => GetMetadata<T>().TableName;

    /// <summary>取得未加方言引号的表名。</summary>
    public static string GetTableName(Type type) => GetMetadata(type).TableName;

    /// <summary>解析显式列名；只有 TypeName 的 Column 标记不改变属性名称。</summary>
    public static string GetColumnName(PropertyInfo property)
        => property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name;

    /// <summary>统一继承、忽略字段及属性名称；不把 SQL Server 类型和默认值混入名称映射。</summary>
    private static EntityMetadata BuildMetadata(Type type)
    {
        var hierarchy = new Stack<Type>();
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
            hierarchy.Push(current);
        var columns = hierarchy.SelectMany(level => level.GetProperties(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
            .Where(property => property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0
                && property.GetCustomAttribute<NotMappedAttribute>() is null)
            .OrderBy(property => property.MetadataToken))
            .Select(property => new EntityColumnMetadata(property, GetColumnName(property))).ToArray();
        if (columns.GroupBy(column => column.PropertyName, StringComparer.Ordinal).Any(group => group.Count() > 1)
            || columns.GroupBy(column => column.ColumnName, StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            throw new InvalidOperationException($"实体 {type.Name} 存在重复属性或物理列映射。");
        var key = columns.SingleOrDefault(column => column.PropertyName == nameof(BaseEntity.Uid))
            ?? throw new InvalidOperationException($"实体 {type.Name} 没有找到主键属性 {nameof(BaseEntity.Uid)}。");
        var table = type.GetCustomAttribute<TableAttribute>();
        return new EntityMetadata
        {
            EntityType = type, SchemaName = table?.Schema ?? "dbo", TableName = table?.Name ?? type.Name,
            KeyName = key.ColumnName, Columns = Array.AsReadOnly(columns),
            AllColumns = columns.Select(column => column.ColumnName).ToArray(),
            InsertColumns = columns.Select(column => column.ColumnName).ToArray(),
            UpdateColumns = columns.Where(column => column.PropertyName is not nameof(BaseEntity.Uid)
                and not nameof(BaseEntity.CreatedBy) and not nameof(BaseEntity.CreatedDate))
                .Select(column => column.ColumnName).ToArray(),
            HasVersion = columns.Any(column => column.PropertyName == nameof(BaseEntity.Version)),
            HasSoftDelete = columns.Any(column => column.PropertyName == nameof(BaseEntity.IsDeleted))
        };
    }
}

using SwiftExport.Core.Common;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>为结构生成与全部仓储缓存同一份 PostgreSQL 映射，保持继承字段的稳定声明顺序。</summary>
public static class EntityMetadataCache
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    /// <summary>读取实体的缓存映射。</summary>
    public static EntityMetadata GetMetadata<T>() => GetMetadata(typeof(T));

    /// <summary>构建并缓存持久化属性，拒绝歧义映射。</summary>
    public static EntityMetadata GetMetadata(Type type) => Cache.GetOrAdd(type, BuildMetadata);

    /// <summary>优先读取显式 PG 列名，否则由 CLR 属性名生成小写物理名；参数及返回别名仍使用 CLR 名。</summary>
    private static string GetColumnName(PropertyInfo property)
        => PostgreSqlNames.FromClr(property.GetCustomAttribute<ColumnAttribute>()?.Name ?? property.Name);

    /// <summary>收集持久化属性并生成唯一 PG 映射，拒绝属性隐藏及名称转换后的物理列碰撞。</summary>
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
            || columns.GroupBy(column => column.ColumnName, StringComparer.Ordinal).Any(group => group.Count() > 1))
            throw new InvalidOperationException($"实体 {type.Name} 存在重复属性或物理列映射。");
        var key = columns.SingleOrDefault(column => column.PropertyName == nameof(BaseEntity.Uid))
            ?? throw new InvalidOperationException($"实体 {type.Name} 没有找到主键属性 {nameof(BaseEntity.Uid)}。");
        var table = type.GetCustomAttribute<TableAttribute>();
        return new EntityMetadata
        {
            EntityType = type, SchemaName = PostgreSqlNames.FromClr(table?.Schema ?? "public"),
            TableName = PostgreSqlNames.FromClr(table?.Name ?? type.Name),
            KeyName = key.ColumnName, Columns = Array.AsReadOnly(columns)
        };
    }
}

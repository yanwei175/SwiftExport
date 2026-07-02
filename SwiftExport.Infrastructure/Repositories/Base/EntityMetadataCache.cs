using SwiftExport.Core.Common;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
namespace SwiftExport.Infrastructure.Repositories.Base;

public static class EntityMetadataCache
{
    private static readonly ConcurrentDictionary<Type, EntityMetadata> Cache = new();

    public static EntityMetadata GetMetadata<T>()
    {
        return GetMetadata(typeof(T));
    }

    public static EntityMetadata GetMetadata(Type type)
    {
        return Cache.GetOrAdd(type, BuildMetadata);
    }

    public static string GetTableName<T>()
    {
        return GetMetadata<T>().TableName;
    }

    public static string GetTableName(Type type)
    {
        return GetMetadata(type).TableName;
    }

    private static EntityMetadata BuildMetadata(Type type)
    {
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Where(p => p.GetCustomAttribute<NotMappedAttribute>() == null)
            .ToArray();

        var tableName =
            type.GetCustomAttribute<TableAttribute>()?.Name
            ?? type.Name;

        var keyProp =
            props.FirstOrDefault(p => p.Name == nameof(BaseEntity.Uid))
            ?? throw new InvalidOperationException(
                $"实体 {type.Name} 没有找到主键属性 {nameof(BaseEntity.Uid)}。");

        var allColumns = props
            .Select(p => p.Name)
            .ToArray();

        var insertColumns = allColumns.ToArray();

        var updateColumns = allColumns
            .Where(p =>
                p != nameof(BaseEntity.Uid) &&
                p != nameof(BaseEntity.CreatedBy) &&
                p != nameof(BaseEntity.CreatedDate))
            .ToArray();

        return new EntityMetadata
        {
            EntityType = type,
            TableName = tableName,
            KeyName = keyProp.Name,
            AllColumns = allColumns,
            InsertColumns = insertColumns,
            UpdateColumns = updateColumns,
            HasVersion = allColumns.Contains(nameof(BaseEntity.Version)),
            HasSoftDelete = allColumns.Contains(nameof(BaseEntity.IsDeleted))
        };
    }
}


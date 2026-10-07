using System.Reflection;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>缓存 PostgreSQL 实体的物理名称与持久化属性；名称不包含 SQL 引号或参数前缀。</summary>
public sealed class EntityMetadata
{
    public required Type EntityType { get; init; }
    public required string SchemaName { get; init; }
    public required string TableName { get; init; }
    public required string KeyName { get; init; }
    public required IReadOnlyList<EntityColumnMetadata> Columns { get; init; }

    /// <summary>按 CLR 属性定位物理映射；错误属性立即失败，不静默生成无效 SQL。</summary>
    public EntityColumnMetadata GetProperty(string propertyName)
        => Columns.Single(column => column.PropertyName == propertyName);
}

/// <summary>分离反射属性、数据库列和绑定参数；参数绑定原实体属性，不跟随物理列重命名。</summary>
public sealed class EntityColumnMetadata(PropertyInfo property, string columnName)
{
    public PropertyInfo Property { get; } = property;
    public string PropertyName => Property.Name;
    public string ColumnName { get; } = columnName;
    public string ParameterName => Property.Name;
}

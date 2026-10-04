using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Infrastructure.DatabaseSchema;

/// <summary>集中既有实体类型、长度和可空性规则；不包含建表SQL或连接逻辑。</summary>
internal static class EntityColumnSchema
{
    /// <summary>读取既有类型注解和约定；返回SQL Server存储描述，供各提供者显式适配。</summary>
    internal static string ResolveStoreType(PropertyInfo property)
    {
        var explicitType = property.GetCustomAttribute<DbColumnAttribute>(true)?.StoreType
            ?? property.GetCustomAttribute<ColumnAttribute>()?.TypeName;
        if (!string.IsNullOrWhiteSpace(explicitType)) return explicitType;

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type.IsEnum) return "int";
        if (type == typeof(Guid)) return "uniqueidentifier";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "bigint";
        if (type == typeof(short)) return "smallint";
        if (type == typeof(byte)) return "tinyint";
        if (type == typeof(bool)) return "bit";
        if (type == typeof(DateTime)) return "datetime2(7)";
        if (type == typeof(DateTimeOffset)) return "datetimeoffset(7)";
        if (type == typeof(decimal)) return ResolveDecimalType(property.Name);
        if (type == typeof(double)) return "float";
        if (type == typeof(byte[])) return "varbinary(max)";
        if (type == typeof(string)) return $"nvarchar({ResolveStringLength(property.Name)})";
        throw new InvalidOperationException($"属性 {property.DeclaringType?.Name}.{property.Name} 缺少可用的数据库列类型。");
    }

    /// <summary>保持原有金额/数量四位、比率/体积六位约定，避免提供者迁移改变精度。</summary>
    private static string ResolveDecimalType(string propertyName)
        => propertyName.Contains("Rate", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Cbm", StringComparison.OrdinalIgnoreCase)
            ? "decimal(18,6)"
            : "decimal(18,4)";

    /// <summary>沿用既有字段长度推断；显式类型注解优先，不在本批收紧历史长度。</summary>
    private static string ResolveStringLength(string propertyName)
    {
        if (propertyName.Contains("Json", StringComparison.OrdinalIgnoreCase)) return "max";
        if (propertyName.Contains("Remark", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Reason", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Note", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Address", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Conclusion", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Requirements", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Description", StringComparison.OrdinalIgnoreCase)) return "2000";
        if (propertyName.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Folder", StringComparison.OrdinalIgnoreCase)) return "1000";
        if (propertyName.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase)) return "128";
        if (propertyName.Contains("Name", StringComparison.OrdinalIgnoreCase)) return "500";
        if (propertyName.Contains("Code", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("No", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Sku", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Type", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Unit", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Status", StringComparison.OrdinalIgnoreCase)) return "300";
        return "500";
    }

    /// <summary>按CLR可空声明决定列约束，不根据现有脏数据放宽。</summary>
    internal static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) return true;
        if (property.PropertyType.IsValueType) return false;
        return new NullabilityInfoContext().Create(property).WriteState != NullabilityState.NotNull;
    }

}

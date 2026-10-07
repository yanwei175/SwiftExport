using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using System.Text.RegularExpressions;
using SwiftExport.Core.DatabaseSchema;

namespace SwiftExport.Infrastructure.DatabaseSchema;

/// <summary>为结构生成和参数绑定提供统一的 PostgreSQL 类型、长度及可空性约定。</summary>
internal static class EntityColumnSchema
{
    /// <summary>读取原生 PG 类型声明，缺省时按 CLR 类型推断；未知声明立即失败，不改变既有精度或时间含义。</summary>
    internal static string ResolveStoreType(PropertyInfo property)
    {
        var explicitType = property.GetCustomAttribute<DbColumnAttribute>(true)?.StoreType
            ?? property.GetCustomAttribute<ColumnAttribute>()?.TypeName;
        if (!string.IsNullOrWhiteSpace(explicitType))
        {
            var declared = explicitType.ToLowerInvariant();
            if (declared is "uuid" or "boolean" or "integer" or "bigint" or "smallint" or "date"
                or "double precision" or "text" or "bytea" or "timestamp(6) with time zone" or "timestamp(6) without time zone"
                || Regex.IsMatch(declared, @"^(numeric\(\d+,\d+\)|varchar\(\d+\))$"))
                return declared;
            throw new InvalidOperationException($"{property.DeclaringType?.Name}.{property.Name}声明了不支持的PG类型：{explicitType}。");
        }

        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        if (type.IsEnum || type == typeof(int)) return "integer";
        if (type == typeof(Guid)) return "uuid";
        if (type == typeof(long)) return "bigint";
        if (type == typeof(short)) return "smallint";
        if (type == typeof(bool)) return "boolean";
        if (type == typeof(DateTime)) return property.Name.EndsWith("Utc", StringComparison.Ordinal)
            ? "timestamp(6) with time zone" : "timestamp(6) without time zone";
        if (type == typeof(DateTimeOffset)) return "timestamp(6) with time zone";
        if (type == typeof(decimal)) return ResolveDecimalType(property.Name);
        if (type == typeof(double)) return "double precision";
        if (type == typeof(byte[])) return "bytea";
        if (type == typeof(string)) return property.Name.Contains("Json", StringComparison.OrdinalIgnoreCase)
            ? "text" : $"varchar({ResolveStringLength(property.Name)})";
        throw new InvalidOperationException($"属性 {property.DeclaringType?.Name}.{property.Name} 缺少可用的PG列类型。");
    }

    /// <summary>金额/数量保持四位、比率/体积保持六位，整理类型声明不调整业务精度。</summary>
    private static string ResolveDecimalType(string propertyName)
        => propertyName.Contains("Rate", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Cbm", StringComparison.OrdinalIgnoreCase)
            ? "numeric(18,6)" : "numeric(18,4)";

    /// <summary>沿用既有字段长度约定；显式类型声明优先，不收紧历史字段。</summary>
    private static int ResolveStringLength(string propertyName)
    {
        if (new[] { "Remark", "Reason", "Note", "Address", "Conclusion", "Requirements", "Description" }
            .Any(part => propertyName.Contains(part, StringComparison.OrdinalIgnoreCase))) return 2000;
        if (propertyName.Contains("Path", StringComparison.OrdinalIgnoreCase)
            || propertyName.Contains("Folder", StringComparison.OrdinalIgnoreCase)) return 1000;
        if (propertyName.Contains("Fingerprint", StringComparison.OrdinalIgnoreCase)) return 128;
        if (propertyName.Contains("Name", StringComparison.OrdinalIgnoreCase)) return 500;
        if (new[] { "Code", "No", "Sku", "Type", "Unit", "Status" }
            .Any(part => propertyName.Contains(part, StringComparison.OrdinalIgnoreCase))) return 300;
        return 500;
    }

    /// <summary>按 CLR 可空声明决定列约束，不根据现有数据放宽。</summary>
    internal static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null) return true;
        if (property.PropertyType.IsValueType) return false;
        return new NullabilityInfoContext().Create(property).WriteState != NullabilityState.NotNull;
    }
}

using System.Data;
using Dapper;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Infrastructure.DatabaseSchema;

namespace SwiftExport.Infrastructure.Repositories.Base;

/// <summary>显式绑定 PostgreSQL 数组和时间参数，避免 Dapper 展开数组或驱动按 Kind 猜测列类型。</summary>
internal sealed class PostgreSqlParameters : SqlMapper.IDynamicParameters
{
    private readonly List<NpgsqlParameter> _parameters = [];

    /// <summary>绑定标量参数；时间参数应经实体映射指定类型。</summary>
    public void Add(string name, object? value) => _parameters.Add(new NpgsqlParameter { ParameterName = name, Value = value ?? DBNull.Value });

    /// <summary>按现有无时区列绑定本地墙上时间，包括空值；不进行时区换算。</summary>
    public void AddTimestamp(string name, DateTime? value) => _parameters.Add(
        new NpgsqlParameter(name, NpgsqlDbType.Timestamp)
        { Value = value.HasValue ? DateTime.SpecifyKind(value.Value, DateTimeKind.Unspecified) : DBNull.Value });

    /// <summary>显式绑定命名 UUID 数组，供多个身份筛选同时使用而不展开参数。</summary>
    public void AddIds(string name, IEnumerable<Guid> values) => _parameters.Add(
        new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = values.ToArray() });

    /// <summary>绑定文本数组，供专用仓储的编码集合查询使用。</summary>
    public void AddTexts(string name, IEnumerable<string> values) => _parameters.Add(
        new NpgsqlParameter(name, NpgsqlDbType.Array | NpgsqlDbType.Text) { Value = values.ToArray() });

    /// <summary>将 Uid 集合绑定为一个 uuid 数组，供 ANY 使用。</summary>
    public static PostgreSqlParameters Ids(IEnumerable<Guid> ids)
    {
        var result = new PostgreSqlParameters();
        result._parameters.Add(new NpgsqlParameter("Uids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = ids.ToArray() });
        return result;
    }

    /// <summary>按生成器的时间列定义绑定实体；UTC 列拒绝无时区值，本地列保留墙上时间。</summary>
    public static PostgreSqlParameters Entity(object entity, EntityMetadata metadata)
    {
        var result = new PostgreSqlParameters();
        foreach (var column in metadata.Columns)
        {
            var value = column.Property.GetValue(entity);
            var type = Nullable.GetUnderlyingType(column.Property.PropertyType) ?? column.Property.PropertyType;
            var parameter = new NpgsqlParameter { ParameterName = column.ParameterName, Value = value ?? DBNull.Value };
            // 实体枚举仍按现有整数列存储，不依赖 PostgreSQL 自定义枚举注册。
            if (type.IsEnum && value is not null)
                parameter.Value = Convert.ChangeType(value, Enum.GetUnderlyingType(type), System.Globalization.CultureInfo.InvariantCulture);
            if (type == typeof(DateTimeOffset))
            {
                parameter.NpgsqlDbType = NpgsqlDbType.TimestampTz;
                if (value is DateTimeOffset offset) parameter.Value = offset.ToUniversalTime();
            }
            if (type == typeof(DateTime))
            {
                var utc = EntityColumnSchema.ResolveStoreType(column.Property).Contains("with time zone", StringComparison.Ordinal);
                if (value is DateTime date)
                {
                    if (utc && date.Kind != DateTimeKind.Utc)
                        throw new ArgumentException($"{column.PropertyName}必须提供UTC时间。");
                    parameter.Value = utc ? date : DateTime.SpecifyKind(date, DateTimeKind.Unspecified);
                }
                parameter.NpgsqlDbType = utc ? NpgsqlDbType.TimestampTz : NpgsqlDbType.Timestamp;
            }
            result._parameters.Add(parameter);
        }
        return result;
    }

    /// <summary>将已确定类型的参数加入当前 Dapper 命令，不接管连接与事务。</summary>
    public void AddParameters(IDbCommand command, SqlMapper.Identity identity)
    {
        // 分页计数和取页会复用参数包；每个命令必须拥有自己的参数实例。
        foreach (var parameter in _parameters) command.Parameters.Add(parameter.Clone());
    }
}

using System;
using System.Linq;
using System.Text;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Core.Entities.Auth;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Tools.DbSeed;

/// <summary>离线投影代码权限目录和真实实体字段，生成可留存的 PG 同步 SQL；不读取配置或连接数据库。</summary>
public static class PermissionCatalogSqlGenerator
{
    /// <summary>生成确定性 SQL；已有定义只更新三展示列，缺失项只建定义，授权关系及其他字段保持不变。</summary>
    public static string Generate()
    {
        var definitions = PermissionCatalog.All;
        if (definitions.Select(item => item.PermissionKey).Distinct(StringComparer.OrdinalIgnoreCase).Count() != definitions.Count)
            throw new InvalidOperationException("代码权限目录包含重复的权限键。");

        var map = EntityMetadataCache.GetMetadata(typeof(SysPermission));
        var roles = EntityMetadataCache.GetMetadata(typeof(SysRole));
        var relations = EntityMetadataCache.GetMetadata(typeof(SysRolePermission));
        string Column(string property) => PostgreSqlNames.Quote(map.GetProperty(property).ColumnName);
        var columns = new[] { nameof(SysPermission.Uid), nameof(SysPermission.Version), nameof(SysPermission.IsDeleted),
            nameof(SysPermission.CreatedDate), nameof(SysPermission.CreatedBy), nameof(SysPermission.UpdatedDate), nameof(SysPermission.UpdatedBy),
            nameof(SysPermission.PermissionKey), nameof(SysPermission.DisplayName), nameof(SysPermission.GroupKey), nameof(SysPermission.SortOrder), nameof(SysPermission.IsActive) };
        var key = Column(nameof(SysPermission.PermissionKey));
        var name = Column(nameof(SysPermission.DisplayName));
        var group = Column(nameof(SysPermission.GroupKey));
        var sort = Column(nameof(SysPermission.SortOrder));
        var sql = new StringBuilder();
        sql.AppendLine($$"""
            -- 自动生成：PermissionCatalog.All（{{definitions.Count}} 项），实体字段来自 EntityMetadataCache。
            -- 生成入口：_tools/DbSeed/同步权限目录.ps1；不要手改目录项，代码目录改变后重新生成。
            -- 本文件不含连接信息。请在已确认的目标库一次执行完整脚本；生成不等于执行。
            -- 只同步定义，不创建账号、不修改用户/角色授权、不删除目录外定义、不迁移结构。
            -- 需要当前 PG 权限结构及内置 gen_random_uuid()；异常时整笔事务失败，不自动重试。
            BEGIN TRANSACTION ISOLATION LEVEL SERIALIZABLE;
            SET LOCAL standard_conforming_strings = on;
            -- 在首个数据快照前沿用角色→权限顺序，协调已有 RBAC 与目录同步用例。
            LOCK TABLE {{Table(roles)}} IN SHARE ROW EXCLUSIVE MODE;
            LOCK TABLE {{Table(map)}} IN SHARE ROW EXCLUSIVE MODE;

            CREATE TEMP TABLE swift_export_permission_catalog (
                permission_key text COLLATE "C" PRIMARY KEY,
                display_name text COLLATE "C" NOT NULL,
                group_key text COLLATE "C" NOT NULL,
                sort_order integer NOT NULL
            ) ON COMMIT DROP;
            INSERT INTO pg_temp.swift_export_permission_catalog (permission_key, display_name, group_key, sort_order)
            VALUES
            """);
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            sql.Append("    (").Append(Literal(definition.PermissionKey)).Append(", ")
                .Append(Literal(definition.DisplayName)).Append(", ").Append(Literal(definition.GroupKey))
                .Append(", ").Append(index + 1).AppendLine(index == definitions.Count - 1 ? ");" : "),");
        }
        sql.AppendLine($$"""

            DO $permission_catalog_sync$
            DECLARE
                inserted_count integer;
                updated_count integer;
                external_count integer;
            BEGIN
                -- 只验证既有结构，不隐式建表、补列或修改持久化约束。
                IF (SELECT count(*) FROM information_schema.columns
                    WHERE table_schema = {{Literal(map.SchemaName)}} AND table_name = {{Literal(map.TableName)}}
                    AND column_name IN ({{string.Join(", ", columns.Select(property => Literal(map.GetProperty(property).ColumnName)))}})) <> {{columns.Length}}
                    OR to_regclass({{Literal(Table(relations))}}) IS NULL
                    OR NOT EXISTS (SELECT 1 FROM information_schema.columns
                        WHERE table_schema = {{Literal(relations.SchemaName)}} AND table_name = {{Literal(relations.TableName)}}
                        AND column_name = {{Literal(relations.GetProperty(nameof(SysRolePermission.PermissionId)).ColumnName)}}) THEN
                    RAISE EXCEPTION '目标库缺少当前权限定义或角色权限关系结构；目录同步不迁移结构。';
                END IF;

                -- 包括停用、删除与目录外定义；不能合并大小写身份或借同步复活已有定义。
                IF EXISTS (SELECT 1 FROM {{Table(map)}}
                    GROUP BY lower({{key}} COLLATE "C") HAVING count(*) > 1) THEN
                    RAISE EXCEPTION '数据库权限定义存在重复键（含大小写）；请显式处理，同步不会合并。';
                END IF;
                IF EXISTS (SELECT 1 FROM {{Table(map)}} AS p
                    JOIN pg_temp.swift_export_permission_catalog AS c
                        ON lower(p.{{key}} COLLATE "C") = lower(c.permission_key)
                    WHERE p.{{key}} COLLATE "C" <> c.permission_key) THEN
                    RAISE EXCEPTION '数据库权限键大小写与代码不一致；请显式迁移，同步不会改写权限键。';
                END IF;

                -- 既有定义仅变更展示三列：保留 Uid、Key、状态、版本、审计及全部关系。
                UPDATE {{Table(map)}} AS p
                SET {{name}} = c.display_name, {{group}} = c.group_key, {{sort}} = c.sort_order
                FROM pg_temp.swift_export_permission_catalog AS c
                WHERE p.{{key}} COLLATE "C" = c.permission_key
                    AND (p.{{name}} COLLATE "C" IS DISTINCT FROM c.display_name
                        OR p.{{group}} COLLATE "C" IS DISTINCT FROM c.group_key
                        OR p.{{sort}} IS DISTINCT FROM c.sort_order);
                GET DIAGNOSTICS updated_count = ROW_COUNT;

                -- 缺失键只建定义；随机身份在实际插入时生成，重复执行不改既有身份。
                INSERT INTO {{Table(map)}} ({{string.Join(", ", columns.Select(Column))}})
                SELECT gen_random_uuid(), 1, FALSE,
                    clock_timestamp() AT TIME ZONE 'UTC', 'DbSeed', clock_timestamp() AT TIME ZONE 'UTC', 'DbSeed',
                    c.permission_key, c.display_name, c.group_key, c.sort_order, TRUE
                FROM pg_temp.swift_export_permission_catalog AS c
                WHERE NOT EXISTS (SELECT 1 FROM {{Table(map)}} AS p WHERE p.{{key}} COLLATE "C" = c.permission_key);
                GET DIAGNOSTICS inserted_count = ROW_COUNT;

                SELECT count(*) INTO external_count FROM {{Table(map)}} AS p
                WHERE NOT EXISTS (SELECT 1 FROM pg_temp.swift_export_permission_catalog AS c WHERE p.{{key}} COLLATE "C" = c.permission_key);
                RAISE NOTICE '权限目录同步完成：新增 %，更新 %；目录外 % 项保留。', inserted_count, updated_count, external_count;
            END;
            $permission_catalog_sync$;
            COMMIT;
            """);
        // 不写时间戳或运行环境；相同代码与元数据跨主机生成相同 UTF-8 内容。
        return sql.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>固定实体元数据标识符使用已有 PG 转义函数，不接收用户提供的表列名。</summary>
    private static string Table(EntityMetadata map) => $"{PostgreSqlNames.Quote(map.SchemaName)}.{PostgreSqlNames.Quote(map.TableName)}";

    /// <summary>只序列化代码目录常量；脚本固定标准字符串模式，单引号不能逃逸为 SQL。</summary>
    private static string Literal(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

using SwiftExport.Core.Interfaces.Systems.Dictionaries.Data;
using Dapper;
using SwiftExport.Infrastructure.DatabaseSchema;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems.Dictionaries.Data;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Contracts.DTOs.Errors;


namespace SwiftExport.Infrastructure.Repositories.Systems.Dictionaries.Data
{
    /// <summary>
    /// PostgreSQL字典明细仓储；类型与明细维护沿用先类型后明细的锁顺序
    /// </summary>
    public class PostgreSqlSysDictDataRepository(RepositoryBaseContext context)
        : PostgreSqlDapperRepository<SysDictData>(context), ISysDictDataRepository
    {
        private const string Columns = """
            "uid" AS "Uid",
            "version" AS "Version",
            "is_deleted" AS "IsDeleted",
            "created_date" AS "CreatedDate",
            "created_by" AS "CreatedBy",
            "updated_date" AS "UpdatedDate",
            "updated_by" AS "UpdatedBy",
            "dict_code" AS "DictCode",
            "data_name_en" AS "DataNameEn",
            "data_name_zh" AS "DataNameZh",
            "remark" AS "Remark",
            "sort_order" AS "SortOrder",
            "css_class" AS "CssClass",
            "is_system" AS "IsSystem",
            "is_enabled" AS "IsEnabled"
        """;

        /// <summary>在共享会话内执行PostgreSQL字典查询，保持分页、软删除及调用方事务边界。</summary>
        public async Task<PagedResponseBase<SysDictDataDto>> GetPagedAsync(SysDictDataSearchRequest request)
        {
            request ??= new SysDictDataSearchRequest();
            var pageIndex = Math.Max(1, request.PageIndex);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
            var parameters = new PostgreSqlParameters();
            parameters.Add("Offset", (pageIndex - 1) * pageSize);
            parameters.Add("PageSize", pageSize);
            var filters = new List<string> { "(is_deleted = FALSE OR is_deleted IS NULL)" };
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                filters.Add("(dict_code LIKE @Keyword ESCAPE '' OR data_name_en LIKE @Keyword ESCAPE '' OR data_name_zh LIKE @Keyword ESCAPE '' OR remark LIKE @Keyword ESCAPE '')");
                parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
            }

            var batchKeywords = request.BatchKeywords?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            if (batchKeywords.Length > 0)
            {
                filters.Add("rtrim(dict_code, ' ') = ANY(@BatchKeywords)");
                parameters.AddTexts("BatchKeywords", batchKeywords);
            }

            var where = string.Join(" AND ", filters);
            var total = await Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM public.sys_dict_data WHERE {where}", parameters, Transaction);
            var rows = await Connection.QueryAsync<SysDictDataDto>($@"
                SELECT {Columns}
                FROM public.sys_dict_data
                WHERE {where}
                ORDER BY dict_code, COALESCE(sort_order, 2147483647), data_name_zh, uid
                LIMIT @PageSize OFFSET @Offset;", parameters, Transaction);
            return new PagedResponseBase<SysDictDataDto>
            {
                Items = rows.AsList(),
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        /// <summary>在共享会话内执行PostgreSQL字典查询，保持分页、软删除及调用方事务边界。</summary>
        public async Task<IReadOnlyList<SysDictData>> GetByCodeAsync(string dictCode)
        {
            var rows = await Connection.QueryAsync<SysDictData>($@"
                SELECT {Columns}
                FROM public.sys_dict_data
                WHERE rtrim(dict_code, ' ') = rtrim(@DictCode, ' ') AND (is_deleted = FALSE OR is_deleted IS NULL)
                ORDER BY COALESCE(sort_order, 2147483647), data_name_zh, uid;",
                new { DictCode = dictCode.Trim() }, Transaction);
            return rows.AsList();
        }

        /// <summary>在共享会话内执行PostgreSQL字典查询，保持分页、软删除及调用方事务边界。</summary>
        public async Task<IReadOnlyList<string>> GetDictCodesWithDataAsync(IEnumerable<string> dictCodes)
        {
            var codes = dictCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (codes.Length == 0) return [];
            var lockHint = GetLockHint(true);
            var rows = await Connection.QueryAsync<string>($@"
                SELECT dict_code
                FROM public.sys_dict_data
                WHERE rtrim(dict_code, ' ') = ANY(@Codes) AND (is_deleted = FALSE OR is_deleted IS NULL) ORDER BY uid{lockHint};",
                CodeParameters(codes), Transaction);
            return rows.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>保留软删除事实供目录判定内置身份；读取不修复数据，维护锁按 Uid 稳定排序。</summary>
        public async Task<IReadOnlyList<SysDictData>> GetForBuiltInMergeAsync(string? dictCode = null, bool withUpdLock = false, CancellationToken cancellationToken = default)
        {
            var lockHint = GetLockHint(withUpdLock);
            var where = string.IsNullOrWhiteSpace(dictCode) ? "" : "WHERE btrim(dict_code, ' ') = @DictCode";
            var rows = await Connection.QueryAsync<SysDictData>(new CommandDefinition($"""
                SELECT {Columns} FROM public.sys_dict_data
                {where} ORDER BY uid{lockHint};
                """, new { DictCode = dictCode?.Trim() }, Transaction, cancellationToken: cancellationToken));
            return rows.AsList();
        }

        /// <summary>按 Uid 稳定锁定目标明细，包括已删除事实；调用方必须先取得类型锁并拥有外层事务。</summary>
        public async Task<IReadOnlyList<SysDictData>> GetByIdsForUpdateAsync(IEnumerable<Guid> ids)
        {
            var lockHint = GetLockHint(true);
            var values = ids.Distinct().ToArray();
            if (values.Length == 0) return [];
            var rows = await Connection.QueryAsync<SysDictData>($"""
                SELECT {Columns} FROM public.sys_dict_data
                WHERE uid = ANY(@Uids) ORDER BY uid{lockHint};
                """, PostgreSqlParameters.Ids(values), Transaction);
            return rows.AsList();
        }

        /// <summary>保存目录认可的内置覆盖或显式同步结果，保留 Guid 和创建审计；失败由外层用例统一回滚。</summary>
        public async Task<RepositoryWriteResult> SaveBuiltInOverrideAsync(SysDictData entity, int expectedVersion, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entity);
            if (Transaction is null) throw new InvalidOperationException("内置字典覆盖必须位于应用层事务中。");
            if (entity.Uid == Guid.Empty || expectedVersion < 0)
                throw new ArgumentException("内置字典身份或预期版本无效。");
            entity.IsDeleted = false;
            entity.IsEnabled = true;
            entity.IsSystem = true;
            if (expectedVersion == 0)
            {
                entity.Version = 1;
                AuditStamp.StampForInsert(entity);
                // 给定身份的主键处理首次保存竞争；同业务键已有旧身份时也拒绝新建，避免断开旧业务引用。
                var rows = await Connection.ExecuteAsync(new CommandDefinition("""
                    INSERT INTO public.sys_dict_data
                        (uid, version, is_deleted, created_date, created_by, updated_date, updated_by,
                         dict_code, data_name_en, data_name_zh, remark, sort_order, css_class, is_system, is_enabled)
                    SELECT @Uid, @Version, @IsDeleted, @CreatedDate, @CreatedBy, @UpdatedDate, @UpdatedBy,
                           @DictCode, @DataNameEn, @DataNameZh, @Remark, @SortOrder, @CssClass, @IsSystem, @IsEnabled
                    WHERE NOT EXISTS (
                        SELECT 1 FROM public.sys_dict_data
                        WHERE btrim(dict_code, ' ') = btrim(@DictCode, ' ')
                          AND btrim(data_name_en, ' ') = btrim(@DataNameEn, ' '))
                    ON CONFLICT DO NOTHING;
                    """, PostgreSqlParameters.Entity(entity, Metadata), Transaction, cancellationToken: cancellationToken));
                return rows == 1
                    ? RepositoryWriteResult.Ok(entity.Uid, 0, 1, "内置字典覆盖保存成功。")
                    : RepositoryWriteResult.Fail(entity.Uid, 0, ServiceErrorCodes.ConcurrencyConflict, "内置字典已被保存，请刷新后重试。");
            }

            AuditStamp.StampForUpdate(entity);
            var parameters = PostgreSqlParameters.Entity(entity, Metadata);
            parameters.Add("ExpectedVersion", expectedVersion);
            // 创建审计与业务键不进入 SET；旧内置记录的异常标记仅在显式保存或同步时恢复。
            var newVersion = await Connection.QuerySingleOrDefaultAsync<int?>(new CommandDefinition("""
                UPDATE public.sys_dict_data SET data_name_zh=@DataNameZh, remark=@Remark,
                    sort_order=@SortOrder, css_class=@CssClass, is_deleted=FALSE, is_system=TRUE, is_enabled=TRUE,
                    updated_date=@UpdatedDate, updated_by=@UpdatedBy, version=version+1
                WHERE uid=@Uid AND version=@ExpectedVersion RETURNING version;
                """, parameters, Transaction, cancellationToken: cancellationToken));
            if (newVersion is null)
                return RepositoryWriteResult.Fail(entity.Uid, expectedVersion, ServiceErrorCodes.ConcurrencyConflict, "内置字典已被修改，请刷新后重试。");
            entity.Version = newVersion.Value;
            // 正版本回执沿用现有 PG 仓储口径；零版本首次保存单独返回 0→1。
            return RepositoryWriteResult.Ok(entity.Uid, newVersion.Value, newVersion.Value, "内置字典覆盖更新成功。");
        }


        /// <inheritdoc />
        public Task<int> CountProductSeriesReferencesAsync(Guid seriesId) => Connection.ExecuteScalarAsync<int>("""
            SELECT count(*) FROM public.product_master
            WHERE is_deleted=FALSE AND product_series_id=@SeriesId
            """, new { SeriesId = seriesId }, Transaction);

        /// <summary>绑定编码集合，避免Dapper把文本数组展开为SQL Server形式的IN参数。</summary>
        private static PostgreSqlParameters CodeParameters(IEnumerable<string> codes)
        {
            var parameters = new PostgreSqlParameters();
            parameters.AddTexts("Codes", codes);
            return parameters;
        }
    }
}

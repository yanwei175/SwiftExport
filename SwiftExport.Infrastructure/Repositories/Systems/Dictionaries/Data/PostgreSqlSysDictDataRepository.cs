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
        /// <summary>绑定编码集合，避免Dapper把文本数组展开为SQL Server形式的IN参数。</summary>
        private static PostgreSqlParameters CodeParameters(IEnumerable<string> codes)
        {
            var parameters = new PostgreSqlParameters();
            parameters.AddTexts("Codes", codes);
            return parameters;
        }
    }
}

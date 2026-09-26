using Dapper;
using SwiftExport.Contracts.DTOs.SystemDtos;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Systems;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

namespace SwiftExport.Infrastructure.Repositories.Systems
{
    /// <summary>
    /// 字典明细仓储实现
    /// </summary>
    public class SysDictDataRepository(RepositoryBaseContext context)
        : DapperRepository<SysDictData>(context), ISysDictDataRepository
    {
        private const string Columns = "Uid, Version, IsDeleted, CreatedDate, CreatedBy, UpdatedDate, UpdatedBy, DictCode, DataNameEn, DataNameZh, Remark, SortOrder, CssClass, IsSystem, IsEnabled";

        public async Task<PagedResponseBase<SysDictDataDto>> GetPagedAsync(SysDictDataSearchRequest request)
        {
            request ??= new SysDictDataSearchRequest();
            var pageIndex = Math.Max(1, request.PageIndex);
            var pageSize = Math.Clamp(request.PageSize <= 0 ? 20 : request.PageSize, 1, 200);
            var parameters = new DynamicParameters(new { Offset = (pageIndex - 1) * pageSize, PageSize = pageSize });
            var filters = new List<string> { "(IsDeleted = 0 OR IsDeleted IS NULL)" };
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                filters.Add("(DictCode LIKE @Keyword OR DataNameEn LIKE @Keyword OR DataNameZh LIKE @Keyword OR Remark LIKE @Keyword)");
                parameters.Add("Keyword", $"%{request.Keyword.Trim()}%");
            }

            var batchKeywords = request.BatchKeywords?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray() ?? [];
            if (batchKeywords.Length > 0)
            {
                filters.Add("DictCode IN @BatchKeywords");
                parameters.Add("BatchKeywords", batchKeywords);
            }

            var where = string.Join(" AND ", filters);
            var total = await Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(1) FROM dbo.SysDictData WHERE {where}", parameters, Transaction);
            var rows = await Connection.QueryAsync<SysDictDataDto>($@"
                SELECT {Columns}
                FROM dbo.SysDictData
                WHERE {where}
                ORDER BY DictCode, ISNULL(SortOrder, 2147483647), DataNameZh, Uid
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", parameters, Transaction);
            return new PagedResponseBase<SysDictDataDto>
            {
                Items = rows.AsList(),
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }

        public async Task<IReadOnlyList<SysDictData>> GetByCodeAsync(string dictCode)
        {
            var rows = await Connection.QueryAsync<SysDictData>($@"
                SELECT {Columns}
                FROM dbo.SysDictData WITH (NOLOCK)
                WHERE DictCode = @DictCode AND (IsDeleted = 0 OR IsDeleted IS NULL)
                ORDER BY ISNULL(SortOrder, 2147483647), DataNameZh, Uid;",
                new { DictCode = dictCode.Trim() }, Transaction);
            return rows.AsList();
        }

        public async Task<IReadOnlyList<string>> GetDictCodesWithDataAsync(IEnumerable<string> dictCodes)
        {
            var codes = dictCodes.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (codes.Length == 0) return [];
            var rows = await Connection.QueryAsync<string>(@"
                SELECT DISTINCT DictCode
                FROM dbo.SysDictData WITH (UPDLOCK, ROWLOCK)
                WHERE DictCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL);",
                new { Codes = codes }, Transaction);
            return rows.AsList();
        }
    }
}

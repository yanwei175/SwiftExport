using SwiftExport.Core.Interfaces.Suppliers.Management;
using Dapper;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;

using SwiftExport.Contracts.DTOs.Suppliers;
using SwiftExport.Contracts.DTOs.Suppliers.Requests;
using SwiftExport.Contracts.Wrappers;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Suppliers.Management
{
    /// <summary>供应商资料仓储，保留专用执行资格命令维护的业务启停状态。</summary>
    public class SupplierRepository(RepositoryBaseContext context) : DapperRepository<Supplier>(context), ISupplierRepository
    {
        /// <summary>普通资料更新不写业务启停状态，保留专用命令形成的供应商资格。</summary>
        protected override IReadOnlyList<string> UpdateProperties => [.. base.UpdateProperties
            .Where(property => !property.Equals(nameof(Supplier.IsActive), StringComparison.OrdinalIgnoreCase))];

        /// <summary>
        /// 按供应商编码查询单条供应商档案。
        /// </summary>
        public async Task<Supplier?> GetByCodeAsync(string supplierCode)
        {
            if (string.IsNullOrWhiteSpace(supplierCode))
                return null;

            var sql = $"SELECT TOP 1 * FROM [{TableName}] WITH(NOLOCK) WHERE SupplierCode = @Code AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            return await Connection.QueryFirstOrDefaultAsync<Supplier>(sql, new { Code = supplierCode.Trim() }, transaction: Transaction);
        }

        public async Task<bool> ExistBySupplierCode(string supplierCode)
        {
            // 查询最小数据（例如数字 1）并使用 = 替代 IN
            var sql = $"SELECT 1 FROM [{TableName}] WHERE SupplierCode = @Code";
            int? result = await Connection.QueryFirstOrDefaultAsync<int?>(sql, new { Code = supplierCode }, transaction: Transaction);

            // 如果 result 有值（即不是 null），则表示存在。
            return result.HasValue;
        }

        public async Task<IReadOnlyList<string>> GetExistingSupplierCodesAsync(IEnumerable<string> supplierCodeList)
        {
            var sql = $"SELECT SupplierCode FROM [{TableName}] WHERE  SupplierCode IN @Codes";
            var rst = await Connection.QueryAsync<string>(sql, new { Codes = supplierCodeList }, transaction: Transaction);
            return [.. rst];
        }

        public async Task<IReadOnlyList<string>> GetSupplierCodesUsedByOtherSuppliersAsync(
            IEnumerable<(Guid Uid, string SupplierCode)> items)
        {
            var materialized = items.ToArray();
            if (materialized.Length == 0) return [];
            var sql = $"""
                SELECT DISTINCT SupplierCode
                FROM [{TableName}]
                WHERE SupplierCode IN @Codes
                  AND Uid NOT IN @Uids;
                """;
            return (await Connection.QueryAsync<string>(sql, new
            {
                Codes = materialized.Select(item => item.SupplierCode),
                Uids = materialized.Select(item => item.Uid),
            }, transaction: Transaction)).ToList();
        }

        public async Task<PagedResponseBase<SupplierDto>> GetPagedAsync(SupplierSearchRequest request)
        {
            request ??= new SupplierSearchRequest();
            var pageIndex = Math.Max(1, request.PageIndex);
            var pageSize = Math.Min(request.PageSize <= 0 ? 20 : request.PageSize, 200);
            var parameters = new DynamicParameters(new
            {
                Offset = (pageIndex - 1) * pageSize,
                PageSize = pageSize,
            });
            var where = new StringBuilder();
            where.AppendLine("WHERE ISNULL(s.IsDeleted, 0) = 0");
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                where.AppendLine("""
                    AND (
                           s.SupplierCode LIKE @Keyword ESCAPE '~'
                        OR s.SupplierName LIKE @Keyword ESCAPE '~'
                        OR s.DocRelateNo LIKE @Keyword ESCAPE '~'
                        OR s.ContactPerson LIKE @Keyword ESCAPE '~'
                    )
                    """);
                parameters.Add("Keyword", SqlLikePattern.Build(request.Keyword));
            }

            var sql = $"""
                SELECT COUNT(1)
                FROM dbo.Supplier s
                {where};

                SELECT
                    s.Uid, s.Version, s.IsDeleted,
                    s.CreatedDate, s.CreatedBy, s.UpdatedDate, s.UpdatedBy,
                    s.SupplierCode, s.SupplierName, s.ZheKou, s.ZheKouBeiZhu,
                    s.DocRelateNo, s.ContactPerson, s.ContactEmail, s.ContactPhone,
                    s.Address, s.Currency, s.TaxRate, s.PaymentTerms, s.DefaultLeadTime
                FROM dbo.Supplier s
                {where}
                ORDER BY s.SupplierCode ASC, s.Uid ASC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
                """;
            using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
            var totalCount = await multi.ReadFirstAsync<int>();
            var suppliers = await multi.ReadAsync<SupplierDto>();
            return new PagedResponseBase<SupplierDto>
            {
                Items = suppliers.ToList(),
                TotalCount = totalCount,
                PageIndex = pageIndex,
                PageSize = pageSize,
            };
        }

        /// <summary>
        /// 按供应商编码列表批量查询供应商。
        /// </summary>
        public async Task<IReadOnlyList<Supplier>> GetBySupplierCodeListAsync(IEnumerable<string> supplierCodeList)
        {
            if (supplierCodeList == null) return [];

            var normalized = supplierCodeList
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (normalized.Count == 0) return [];

            var sql = $"SELECT * FROM [{TableName}] WITH(NOLOCK) WHERE SupplierCode IN @Codes AND (IsDeleted = 0 OR IsDeleted IS NULL)";
            var rst = await Connection.QueryAsync<Supplier>(sql, new { Codes = normalized }, transaction: Transaction);
            return [.. rst];
        }


    }
}

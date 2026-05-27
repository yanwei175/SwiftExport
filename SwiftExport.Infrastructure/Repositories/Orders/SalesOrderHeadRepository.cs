using Dapper;
using Microsoft.Extensions.Logging;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Common;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Data;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Orders
{
    public class SalesOrderHeadRepository(RepositoryBaseContext context) : DapperRepository<SalesOrderHeader>(context), ISalesOrderHeadRepository
    {
        public async Task<PagedResultDto<SalesOrderHeaderDto>> GetSalesOrderHeaderPagedListAsync(SalesOrderManagerSearchDto request,string orderBy = "h.CreatedDate DESC")
        {
            request ??= new SalesOrderManagerSearchDto();

            if (string.IsNullOrWhiteSpace(orderBy))
            {
                orderBy = "h.CreatedDate DESC";
            }
            else
            {
                orderBy = OrderByRegex().Replace(orderBy, string.Empty);
                if (string.IsNullOrWhiteSpace(orderBy))
                {
                    orderBy = "h.CreatedDate DESC";
                }
            }

            var parameters = new DynamicParameters();


            var sb = new StringBuilder();

            sb.Append("WHERE (h.IsDeleted = 0 OR h.IsDeleted IS NULL)");
            

            if (!string.IsNullOrWhiteSpace(request.CustomerCode))
            {
                parameters.Add("@CustomerCode", request.CustomerCode.Trim());
                sb.Append(" AND h.CustomerCode = @CustomerCode");
            }
            if (request.ApprovalStatuses != null && request.ApprovalStatuses.Count > 0
                && !request.ApprovalStatuses.Any(x => x < 0))
            {
                parameters.Add("@ApprovalStatuses", request.ApprovalStatuses);
                sb.Append(" AND h.ApprovalStatus IN @ApprovalStatuses");
            }
            if (request.OrderStatuses != null && request.OrderStatuses.Count>0 && !request.OrderStatuses.Any(x=>x< 0))
            {
                parameters.Add("@OrderStatus", request.OrderStatuses);
                sb.Append(" AND h.OrderStatus IN @OrderStatus");
            }
            if (!string.IsNullOrWhiteSpace(request.OrderNo))
            {
                parameters.Add("@OrderNo", $"%{request.OrderNo.Trim()}%");
                sb.Append(" AND h.OrderNo LIKE @OrderNo");
            }
            if(!string.IsNullOrWhiteSpace(request.InvoiceNo))
            {
                parameters.Add("@InvoiceNo", $"%{request.InvoiceNo.Trim()}%");
                sb.Append(" AND h.InvoiceNo LIKE @InvoiceNo");
            }
            if (request.OrderId.HasValue && request.OrderId.Value > 0)
            {
                parameters.Add("@OrderId", request.OrderId.Value);
                sb.Append(" AND h.Id = @OrderId");
            }

            if(request.OrderDateFrom.HasValue)
            {
                parameters.Add("@OrderDateFrom", request.OrderDateFrom.Value.Date);
                sb.Append(" AND h.OrderDate >= @OrderDateFrom");
            } 
            if (request.OrderDateTo.HasValue)
            {
                parameters.Add("@OrderDateTo", request.OrderDateTo.Value.Date);
                sb.Append(" AND h.OrderDate <= @OrderDateTo");
            }

            // ① BatchCustomerSkuList
            if (request.BatchCustomerSkuList?.Count > 0)
            {
                parameters.Add("@BatchCustomerSkuList", request.BatchCustomerSkuList);
                sb.Append(@" AND EXISTS (
                                        SELECT 1 FROM [SalesOrderLine] l WITH(NOLOCK)
                                        WHERE l.SalesOrderId = h.Id
                                        AND l.CustomerSku IN @BatchCustomerSkuList)");
            }

            // ② 单关键词模糊
            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                parameters.Add("@Keyword", $"%{request.Keyword.Trim()}%");
                sb.Append(@" AND EXISTS (
                                        SELECT 1 FROM [SalesOrderLine] l WITH(NOLOCK)
                                        WHERE l.SalesOrderId = h.Id
                                        AND (l.Sku LIKE @Keyword OR l.DescriptionEn LIKE @Keyword
                                          OR l.ColorNameEn LIKE @Keyword OR l.CustomerSku LIKE @Keyword
                                          OR l.LinkSku LIKE @Keyword))");
            }

            // ③ 批量关键词模糊
            var btKeywords = request.BatchKeywords?.Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            if (btKeywords?.Count > 0)
            {
                var existsGroups = new List<string>();
                for (int i = 0; i < btKeywords.Count; i++)
                {
                    var paramName = $"@Keyword_{i}";
                    parameters.Add(paramName, $"%{btKeywords[i].Trim()}%");

                    existsGroups.Add($@"EXISTS (
                                        SELECT 1 FROM [SalesOrderLine] l WITH(NOLOCK)
                                        WHERE l.SalesOrderId = h.Id
                                        AND (l.Sku LIKE {paramName} OR l.CustomerSku LIKE {paramName}
                                          OR l.DescriptionEn LIKE {paramName} OR l.LinkSku LIKE {paramName}))");
                                            }
                                            sb.Append(" AND (").Append(string.Join(" OR ", existsGroups)).Append(')');
            }

            int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
            int pageSize = request.PageSize > 0 ? request.PageSize : 50;

            int offset = (pageIndex - 1) * pageSize;
            parameters.Add("@Offset", offset);
            parameters.Add("@Limit", pageSize);

            var joinSql = "FROM [SalesOrderHeader] h WITH(NOLOCK)";

            string whereSql = sb.ToString();

            // 中文注释：映射字段按 SalesOrderLineDto 语义组织，前端可直接绑定/回填。
            string sql = $@"
                            SELECT COUNT(1)
                            {joinSql}
                            {whereSql};

                            SELECT h.*
                            {joinSql}
                            {whereSql}
                            ORDER BY {orderBy}
                            OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY;";

            using var multi = await Connection.QueryMultipleAsync(sql, parameters, transaction: Transaction);
            var total = await multi.ReadFirstAsync<int>();
            var items = await multi.ReadAsync<SalesOrderHeaderDto>();

            return new PagedResultDto<SalesOrderHeaderDto>
            {
                Items = [.. items],
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };

        }
    }
}

using Dapper;
using SwiftExport.Contracts.DTOs.SalesOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Orders;
using SwiftExport.Core.Interfaces;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Base;
using System.Data;
using System.Text;

namespace SwiftExport.Infrastructure.Repositories.Orders
{
    public class SalesOrderHeadRepository(RepositoryBaseContext context) : DapperRepository<SalesOrderHeader>(context), ISalesOrderHeadRepository
    {


        public async Task<PagedResultDto<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerPagedListAsync(SalesOrderManagerSearchDto request)
        {
            request ??= new SalesOrderManagerSearchDto();

            string orderBy = $"h.{nameof(SalesOrderHeader.CreatedDate)} DESC";

            var parameters = new DynamicParameters();

            var sb = new StringBuilder();

            sb.Append($@" WHERE (h.{nameof(SalesOrderHeader.IsDeleted)} = 0 OR h.{nameof(SalesOrderHeader.IsDeleted)} IS NULL)");

            if (!string.IsNullOrWhiteSpace(request.CustomerCode))
            {
                parameters.Add("@CustomerCode", request.CustomerCode.Trim());
                sb.Append($@" AND h.{nameof(SalesOrderHeader.CustomerCode)} = @CustomerCode");
            }

            if (request.ConfirmStatuses != null &&
                request.ConfirmStatuses.Count > 0 &&
                !request.ConfirmStatuses.Any(x => x < 0))
            {
                parameters.Add("@ConfirmationStatuses", request.ConfirmStatuses);
                sb.Append($@" AND h.{nameof(SalesOrderHeader.ConfirmationStatus)} IN @ConfirmationStatuses");
            }

            if (request.OrderStatuses != null &&
                request.OrderStatuses.Count > 0 &&
                !request.OrderStatuses.Any(x => x < 0))
            {
                parameters.Add("@OrderStatuses", request.OrderStatuses);
                sb.Append($@" AND h.{nameof(SalesOrderHeader.OrderStatus)} IN @OrderStatuses");
            }

            if (!string.IsNullOrWhiteSpace(request.OrderNo))
            {
                parameters.Add("@OrderNo", $"%{request.OrderNo.Trim()}%");
                sb.Append($@" AND h.{nameof(SalesOrderHeader.OrderNo)} LIKE @OrderNo");
            }

            if (!string.IsNullOrWhiteSpace(request.InvoiceNo))
            {
                parameters.Add("@InvoiceNo", $"%{request.InvoiceNo.Trim()}%");
                sb.Append($@" AND h.{nameof(SalesOrderHeader.InvoiceNo)} LIKE @InvoiceNo");
            }

            if (request.OrderDateFrom.HasValue)
            {
                parameters.Add("@OrderDateFrom", request.OrderDateFrom.Value.Date);
                sb.Append($@" AND h.{nameof(SalesOrderHeader.OrderDate)} >= @OrderDateFrom");
            }

            if (request.OrderDateTo.HasValue)
            {
                parameters.Add("@OrderDateTo", request.OrderDateTo.Value.Date.AddDays(1));
                sb.Append($@" AND h.{nameof(SalesOrderHeader.OrderDate)} < @OrderDateTo");
            }

            if (request.BatchCustomerSkuList?.Count > 0)
            {
                parameters.Add("@BatchCustomerSkuList", request.BatchCustomerSkuList);

                sb.Append($@"
                             AND EXISTS (
                                    SELECT 1
                                    FROM [SalesOrderLine] l WITH(NOLOCK)
                                    WHERE l.{nameof(SalesOrderLine.SalesOrderId)} = h.{nameof(SalesOrderHeader.Uid)}
                                      AND l.{nameof(SalesOrderLine.CustomerSku)} IN @BatchCustomerSkuList
                             )");
            }

            if (!string.IsNullOrWhiteSpace(request.Keyword))
            {
                parameters.Add("@Keyword", $"%{request.Keyword.Trim()}%");

                sb.Append($@"
                             AND EXISTS (
                                    SELECT 1
                                    FROM [SalesOrderLine] l WITH(NOLOCK)
                                    WHERE l.{nameof(SalesOrderLine.SalesOrderId)} = h.{nameof(SalesOrderHeader.Uid)}
                                      AND (
                                            l.{nameof(SalesOrderLine.Sku)} LIKE @Keyword
                                         OR l.{nameof(SalesOrderLine.DescriptionEn)} LIKE @Keyword
                                         OR l.{nameof(SalesOrderLine.ColorNameEn)} LIKE @Keyword
                                         OR l.{nameof(SalesOrderLine.CustomerSku)} LIKE @Keyword
                                         OR l.{nameof(SalesOrderLine.LinkSku)} LIKE @Keyword
                                      )
                             )");
            }

            var btKeywords = request.BatchKeywords?
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .ToList();

            if (btKeywords?.Count > 0)
            {
                var existsGroups = new List<string>();

                for (int i = 0; i < btKeywords.Count; i++)
                {
                    var paramName = $"@Keyword_{i}";
                    parameters.Add(paramName, $"%{btKeywords[i]}%");

                    existsGroups.Add($@"
                                        EXISTS (
                                                SELECT 1
                                                FROM [SalesOrderLine] l WITH(NOLOCK)
                                                WHERE l.{nameof(SalesOrderLine.SalesOrderId)} = h.{nameof(SalesOrderHeader.Uid)}
                                                  AND (
                                                        l.{nameof(SalesOrderLine.Sku)} LIKE {paramName}
                                                     OR l.{nameof(SalesOrderLine.CustomerSku)} LIKE {paramName}
                                                     OR l.{nameof(SalesOrderLine.DescriptionEn)} LIKE {paramName}
                                                     OR l.{nameof(SalesOrderLine.ColorNameEn)} LIKE {paramName}
                                                     OR l.{nameof(SalesOrderLine.LinkSku)} LIKE {paramName}
                                                  )
                                        )");
                }

                sb.Append(" AND (")
                  .Append(string.Join(" OR ", existsGroups))
                  .Append(')');
            }

            int pageIndex = request.PageIndex > 0 ? request.PageIndex : 1;
            int pageSize = request.PageSize > 0 ? request.PageSize : 50;
            int offset = (pageIndex - 1) * pageSize;

            parameters.Add("@Offset", offset);
            parameters.Add("@Limit", pageSize);
            parameters.Add("@SourceType", OrderTypesEnum.销售订单);

            var joinSql = "FROM [SalesOrderHeader] h WITH(NOLOCK)";
            var whereSql = sb.ToString();

            string sql = $@"SELECT COUNT(1)
                            {joinSql}
                            {whereSql};

                            ;WITH PagedHeaders AS
                            (
                                SELECT h.*
                                {joinSql}
                                {whereSql}
                                ORDER BY {orderBy}
                                OFFSET @Offset ROWS FETCH NEXT @Limit ROWS ONLY
                            )
                            SELECT
                                h.{nameof(SalesOrderHeader.Uid)} AS {nameof(SalesOrderHeaderManagerDto.Uid)},
                                h.{nameof(SalesOrderHeader.Version)} AS {nameof(SalesOrderHeaderManagerDto.Version)},
                                h.{nameof(SalesOrderHeader.CreatedBy)} AS {nameof(SalesOrderHeaderManagerDto.CreatedBy)},
                                h.{nameof(SalesOrderHeader.CreatedDate)} AS {nameof(SalesOrderHeaderManagerDto.CreatedDate)},
                                h.{nameof(SalesOrderHeader.UpdatedBy)} AS {nameof(SalesOrderHeaderManagerDto.UpdatedBy)},
                                h.{nameof(SalesOrderHeader.UpdatedDate)} AS {nameof(SalesOrderHeaderManagerDto.UpdatedDate)},
                                h.{nameof(SalesOrderHeader.OrderStatus)} AS {nameof(SalesOrderHeaderManagerDto.OrderStatus)},
                                h.{nameof(SalesOrderHeader.ConfirmationStatus)} AS {nameof(SalesOrderHeaderManagerDto.ConfirmationStatus)},
                                h.{nameof(SalesOrderHeader.OrderNo)} AS {nameof(SalesOrderHeaderManagerDto.OrderNo)},
                                h.{nameof(SalesOrderHeader.InvoiceNo)} AS {nameof(SalesOrderHeaderManagerDto.InvoiceNo)},
                                h.{nameof(SalesOrderHeader.CustomerId)} AS {nameof(SalesOrderHeaderManagerDto.CustomerId)},
                                h.{nameof(SalesOrderHeader.CustomerCode)} AS {nameof(SalesOrderHeaderManagerDto.CustomerCode)},
                                h.{nameof(SalesOrderHeader.CustomerPoNo)} AS {nameof(SalesOrderHeaderManagerDto.CustomerPoNo)},

                                ISNULL(x.TotalQty, 0) AS {nameof(SalesOrderHeaderManagerDto.TotalQty)},
                                ISNULL(x.TotalShippedQty, 0) AS {nameof(SalesOrderHeaderManagerDto.TotalShippedQty)},
                                ISNULL(x.TotalQty, 0) - ISNULL(x.TotalShippedQty, 0) AS {nameof(SalesOrderHeaderManagerDto.TotalUnshippedQty)},

                                h.{nameof(SalesOrderHeader.Currency)} AS {nameof(SalesOrderHeaderManagerDto.Currency)},
                                h.{nameof(SalesOrderHeader.ExchangeRate)} AS {nameof(SalesOrderHeaderManagerDto.ExchangeRate)},
                                h.{nameof(SalesOrderHeader.TotalAmountOriginalCurrency)} AS {nameof(SalesOrderHeaderManagerDto.TotalAmountOriginalCurrency)},
                                h.{nameof(SalesOrderHeader.TotalAmountLocalCurrency)} AS {nameof(SalesOrderHeaderManagerDto.TotalAmountLocalCurrency)},

                                h.{nameof(SalesOrderHeader.OrderDate)} AS {nameof(SalesOrderHeaderManagerDto.OrderDate)},
                                h.{nameof(SalesOrderHeader.DeliveryDate)} AS {nameof(SalesOrderHeaderManagerDto.DeliveryDate)},
                                h.{nameof(SalesOrderHeader.ReplyDate)} AS {nameof(SalesOrderHeaderManagerDto.ReplyDate)},

                                h.{nameof(SalesOrderHeader.SoFolder)} AS {nameof(SalesOrderHeaderManagerDto.SoFolder)},
                                h.{nameof(SalesOrderHeader.Remark)} AS {nameof(SalesOrderHeaderManagerDto.Remark)}
                            FROM PagedHeaders h
                            OUTER APPLY
                            (
                                SELECT
                                    SUM(l.{nameof(SalesOrderLine.Qty)}) AS TotalQty,
                                    SUM(ISNULL(d.ShippedQty, 0)) AS TotalShippedQty
                                FROM [SalesOrderLine] l WITH(NOLOCK)
                                LEFT JOIN [IndependentDemand] d WITH(NOLOCK)
                                    ON d.{nameof(IndependentDemand.SourceType)} = @SourceType
                                   AND d.{nameof(IndependentDemand.SourceLineId)} = l.{nameof(SalesOrderLine.Uid)}
                                WHERE l.{nameof(SalesOrderLine.SalesOrderId)} = h.{nameof(SalesOrderHeader.Uid)}
                            ) x
                            ORDER BY h.{nameof(SalesOrderHeader.CreatedDate)} DESC;";

            using var multi = await Connection.QueryMultipleAsync(
                sql,
                parameters,
                transaction: Transaction);

            var total = await multi.ReadFirstAsync<int>();
            var items = await multi.ReadAsync<SalesOrderHeaderManagerDto>();

            return new PagedResultDto<SalesOrderHeaderManagerDto>
            {
                Items = items.ToList(),
                TotalCount = total,
                PageIndex = pageIndex,
                PageSize = pageSize
            };
        }


        public async Task<SalesOrderHeaderManagerDto?> GetSalesOrderHeaderManagerByIdAsync(Guid orderId)
        {
            var sql = $@"SELECT 
                            h.{nameof(SalesOrderHeader.Uid)} AS {nameof(SalesOrderHeaderManagerDto.Uid)},
                            h.{nameof(SalesOrderHeader.Version)} AS {nameof(SalesOrderHeaderManagerDto.Version)},
                            h.{nameof(SalesOrderHeader.CreatedBy)} AS {nameof(SalesOrderHeaderManagerDto.CreatedBy)},
                            h.{nameof(SalesOrderHeader.CreatedDate)} AS {nameof(SalesOrderHeaderManagerDto.CreatedDate)},
                            h.{nameof(SalesOrderHeader.UpdatedBy)} AS {nameof(SalesOrderHeaderManagerDto.UpdatedBy)},
                            h.{nameof(SalesOrderHeader.UpdatedDate)} AS {nameof(SalesOrderHeaderManagerDto.UpdatedDate)},

                            h.{nameof(SalesOrderHeader.OrderStatus)} AS {nameof(SalesOrderHeaderManagerDto.OrderStatus)},
                            h.{nameof(SalesOrderHeader.ConfirmationStatus)} AS {nameof(SalesOrderHeaderManagerDto.ConfirmationStatus)},
                            h.{nameof(SalesOrderHeader.OrderNo)} AS {nameof(SalesOrderHeaderManagerDto.OrderNo)},
                            h.{nameof(SalesOrderHeader.InvoiceNo)} AS {nameof(SalesOrderHeaderManagerDto.InvoiceNo)},
                            h.{nameof(SalesOrderHeader.CustomerId)} AS {nameof(SalesOrderHeaderManagerDto.CustomerId)},
                            h.{nameof(SalesOrderHeader.CustomerCode)} AS {nameof(SalesOrderHeaderManagerDto.CustomerCode)},
                            h.{nameof(SalesOrderHeader.CustomerPoNo)} AS {nameof(SalesOrderHeaderManagerDto.CustomerPoNo)},

                            ISNULL(SUM(l.{nameof(SalesOrderLine.Qty)}), 0) AS {nameof(SalesOrderHeaderManagerDto.TotalQty)},
                            ISNULL(SUM(d.{nameof(IndependentDemand.ShippedQty)}), 0) AS {nameof(SalesOrderHeaderManagerDto.TotalShippedQty)},
                            ISNULL(SUM(l.{nameof(SalesOrderLine.Qty)}), 0) - ISNULL(SUM(d.{nameof(IndependentDemand.ShippedQty)}), 0) AS {nameof(SalesOrderHeaderManagerDto.TotalUnshippedQty)},

                            h.{nameof(SalesOrderHeader.Currency)} AS {nameof(SalesOrderHeaderManagerDto.Currency)},
                            h.{nameof(SalesOrderHeader.ExchangeRate)} AS {nameof(SalesOrderHeaderManagerDto.ExchangeRate)},
                            h.{nameof(SalesOrderHeader.TotalAmountOriginalCurrency)} AS {nameof(SalesOrderHeaderManagerDto.TotalAmountOriginalCurrency)},
                            h.{nameof(SalesOrderHeader.TotalAmountLocalCurrency)} AS {nameof(SalesOrderHeaderManagerDto.TotalAmountLocalCurrency)},
                            h.{nameof(SalesOrderHeader.OrderDate)} AS {nameof(SalesOrderHeaderManagerDto.OrderDate)},
                            h.{nameof(SalesOrderHeader.DeliveryDate)} AS {nameof(SalesOrderHeaderManagerDto.DeliveryDate)},
                            h.{nameof(SalesOrderHeader.ReplyDate)} AS {nameof(SalesOrderHeaderManagerDto.ReplyDate)},
                            h.{nameof(SalesOrderHeader.SoFolder)} AS {nameof(SalesOrderHeaderManagerDto.SoFolder)},
                            h.{nameof(SalesOrderHeader.Remark)} AS {nameof(SalesOrderHeaderManagerDto.Remark)}
                        FROM [SalesOrderHeader] h
                        LEFT JOIN [SalesOrderLine] l 
                            ON l.{nameof(SalesOrderLine.SalesOrderId)} = h.{nameof(SalesOrderHeader.Uid)}
                        LEFT JOIN [IndependentDemand] d 
                            ON d.{nameof(IndependentDemand.SourceType)} = @SourceType 
                           AND d.{nameof(IndependentDemand.SourceLineId)} = l.{nameof(SalesOrderLine.Uid)}
                        WHERE h.{nameof(SalesOrderHeader.Uid)} = @OrderId 
                          AND (h.{nameof(SalesOrderHeader.IsDeleted)} = 0 OR h.{nameof(SalesOrderHeader.IsDeleted)} IS NULL)
                        GROUP BY
                            h.{nameof(SalesOrderHeader.Uid)},
                            h.{nameof(SalesOrderHeader.Version)},
                            h.{nameof(SalesOrderHeader.CreatedBy)},
                            h.{nameof(SalesOrderHeader.CreatedDate)},
                            h.{nameof(SalesOrderHeader.UpdatedBy)},
                            h.{nameof(SalesOrderHeader.UpdatedDate)},
                            h.{nameof(SalesOrderHeader.OrderStatus)},
                            h.{nameof(SalesOrderHeader.ConfirmationStatus)},
                            h.{nameof(SalesOrderHeader.OrderNo)},
                            h.{nameof(SalesOrderHeader.InvoiceNo)},
                            h.{nameof(SalesOrderHeader.CustomerId)},
                            h.{nameof(SalesOrderHeader.CustomerCode)},
                            h.{nameof(SalesOrderHeader.CustomerPoNo)},
                            h.{nameof(SalesOrderHeader.Currency)},
                            h.{nameof(SalesOrderHeader.ExchangeRate)},
                            h.{nameof(SalesOrderHeader.TotalAmountOriginalCurrency)},
                            h.{nameof(SalesOrderHeader.TotalAmountLocalCurrency)},
                            h.{nameof(SalesOrderHeader.OrderDate)},
                            h.{nameof(SalesOrderHeader.DeliveryDate)},
                            h.{nameof(SalesOrderHeader.ReplyDate)},
                            h.{nameof(SalesOrderHeader.SoFolder)},
                            h.{nameof(SalesOrderHeader.Remark)};";

            var parameters = new
            {
                OrderId = orderId,
                SourceType = OrderTypesEnum.销售订单
            };

            return await Connection.QueryFirstOrDefaultAsync<SalesOrderHeaderManagerDto>(
                sql, parameters, transaction: Transaction);
        }

        public async Task<List<SalesOrderHeaderManagerDto>> GetSalesOrderHeaderManagerListByIdListAsync(List<Guid> uidList)
        {
            var sql = $@"SELECT 
                            h.{nameof(SalesOrderHeader.Uid)} AS {nameof(SalesOrderHeaderManagerDto.Uid)},
                            h.{nameof(SalesOrderHeader.Version)} AS {nameof(SalesOrderHeaderManagerDto.Version)},
                            h.{nameof(SalesOrderHeader.CreatedBy)} AS {nameof(SalesOrderHeaderManagerDto.CreatedBy)},
                            h.{nameof(SalesOrderHeader.CreatedDate)} AS {nameof(SalesOrderHeaderManagerDto.CreatedDate)},
                            h.{nameof(SalesOrderHeader.UpdatedBy)} AS {nameof(SalesOrderHeaderManagerDto.UpdatedBy)},
                            h.{nameof(SalesOrderHeader.UpdatedDate)} AS {nameof(SalesOrderHeaderManagerDto.UpdatedDate)},

                            h.{nameof(SalesOrderHeader.OrderStatus)} AS {nameof(SalesOrderHeaderManagerDto.OrderStatus)},
                            h.{nameof(SalesOrderHeader.ConfirmationStatus)} AS {nameof(SalesOrderHeaderManagerDto.ConfirmationStatus)},
                            h.{nameof(SalesOrderHeader.OrderNo)} AS {nameof(SalesOrderHeaderManagerDto.OrderNo)},
                            h.{nameof(SalesOrderHeader.InvoiceNo)} AS {nameof(SalesOrderHeaderManagerDto.InvoiceNo)},
                            h.{nameof(SalesOrderHeader.CustomerId)} AS {nameof(SalesOrderHeaderManagerDto.CustomerId)},
                            h.{nameof(SalesOrderHeader.CustomerCode)} AS {nameof(SalesOrderHeaderManagerDto.CustomerCode)},
                            h.{nameof(SalesOrderHeader.CustomerPoNo)} AS {nameof(SalesOrderHeaderManagerDto.CustomerPoNo)},

                            ISNULL(SUM(l.{nameof(SalesOrderLine.Qty)}), 0) AS {nameof(SalesOrderHeaderManagerDto.TotalQty)},
                            ISNULL(SUM(d.{nameof(IndependentDemand.ShippedQty)}), 0) AS {nameof(SalesOrderHeaderManagerDto.TotalShippedQty)},
                            ISNULL(SUM(l.{nameof(SalesOrderLine.Qty)}), 0) - ISNULL(SUM(d.{nameof(IndependentDemand.ShippedQty)}), 0) AS {nameof(SalesOrderHeaderManagerDto.TotalUnshippedQty)},

                            h.{nameof(SalesOrderHeader.Currency)} AS {nameof(SalesOrderHeaderManagerDto.Currency)},
                            h.{nameof(SalesOrderHeader.ExchangeRate)} AS {nameof(SalesOrderHeaderManagerDto.ExchangeRate)},
                            h.{nameof(SalesOrderHeader.TotalAmountOriginalCurrency)} AS {nameof(SalesOrderHeaderManagerDto.TotalAmountOriginalCurrency)},
                            h.{nameof(SalesOrderHeader.TotalAmountLocalCurrency)} AS {nameof(SalesOrderHeaderManagerDto.TotalAmountLocalCurrency)},
                            h.{nameof(SalesOrderHeader.OrderDate)} AS {nameof(SalesOrderHeaderManagerDto.OrderDate)},
                            h.{nameof(SalesOrderHeader.DeliveryDate)} AS {nameof(SalesOrderHeaderManagerDto.DeliveryDate)},
                            h.{nameof(SalesOrderHeader.ReplyDate)} AS {nameof(SalesOrderHeaderManagerDto.ReplyDate)},
                            h.{nameof(SalesOrderHeader.SoFolder)} AS {nameof(SalesOrderHeaderManagerDto.SoFolder)},
                            h.{nameof(SalesOrderHeader.Remark)} AS {nameof(SalesOrderHeaderManagerDto.Remark)}
                        FROM [SalesOrderHeader] h
                        LEFT JOIN [SalesOrderLine] l 
                            ON l.{nameof(SalesOrderLine.SalesOrderId)} = h.{nameof(SalesOrderHeader.Uid)}
                        LEFT JOIN [IndependentDemand] d 
                            ON d.{nameof(IndependentDemand.SourceType)} = @SourceType 
                           AND d.{nameof(IndependentDemand.SourceLineId)} = l.{nameof(SalesOrderLine.Uid)}
                        WHERE h.{nameof(SalesOrderHeader.Uid)} IN @UidList 
                          AND (h.{nameof(SalesOrderHeader.IsDeleted)} = 0 OR h.{nameof(SalesOrderHeader.IsDeleted)} IS NULL)
                        GROUP BY
                            h.{nameof(SalesOrderHeader.Uid)},
                            h.{nameof(SalesOrderHeader.Version)},
                            h.{nameof(SalesOrderHeader.CreatedBy)},
                            h.{nameof(SalesOrderHeader.CreatedDate)},
                            h.{nameof(SalesOrderHeader.UpdatedBy)},
                            h.{nameof(SalesOrderHeader.UpdatedDate)},
                            h.{nameof(SalesOrderHeader.OrderStatus)},
                            h.{nameof(SalesOrderHeader.ConfirmationStatus)},
                            h.{nameof(SalesOrderHeader.OrderNo)},
                            h.{nameof(SalesOrderHeader.InvoiceNo)},
                            h.{nameof(SalesOrderHeader.CustomerId)},
                            h.{nameof(SalesOrderHeader.CustomerCode)},
                            h.{nameof(SalesOrderHeader.CustomerPoNo)},
                            h.{nameof(SalesOrderHeader.Currency)},
                            h.{nameof(SalesOrderHeader.ExchangeRate)},
                            h.{nameof(SalesOrderHeader.TotalAmountOriginalCurrency)},
                            h.{nameof(SalesOrderHeader.TotalAmountLocalCurrency)},
                            h.{nameof(SalesOrderHeader.OrderDate)},
                            h.{nameof(SalesOrderHeader.DeliveryDate)},
                            h.{nameof(SalesOrderHeader.ReplyDate)},
                            h.{nameof(SalesOrderHeader.SoFolder)},
                            h.{nameof(SalesOrderHeader.Remark)};";

            var parameters = new
            {
                UidList = uidList,
                SourceType = OrderTypesEnum.销售订单
            };

            var rst = await Connection.QueryAsync<SalesOrderHeaderManagerDto>(
                sql, parameters, transaction: Transaction);
            return [.. rst];
        }

        public async Task<bool> DeleteSalesOrderAsync(Guid orderId, int version)
        {

            const string deleteHeaderSql = $@"
                                        UPDATE [SalesOrderHeader]
                                        SET 
                                            IsDeleted = 1,
                                            UpdatedDate = @UpdatedDate,
                                            {nameof(SalesOrderHeader.OrderStatus)} = @OrderStatus,
                                            Version = Version + 1
                                        WHERE {nameof(SalesOrderHeader.Uid)} = @OrderId
                                          AND Version = @Version
                                          AND IsDeleted = 0;";

            const string deleteLinesSql = $@"
                                        UPDATE [SalesOrderLine]
                                        SET 
                                            IsDeleted = 1,
                                            UpdatedDate = @UpdatedDate,
                                            Version = Version + 1
                                        WHERE {nameof(SalesOrderLine.SalesOrderId)} = @OrderId
                                          AND IsDeleted = 0;";

            var updatedDate = DateTime.Now;

            // 先删头，确认并发没问题，再删行更安全
            var headerRows = await Connection.ExecuteAsync(
                deleteHeaderSql,
                new
                {
                    OrderId = orderId,
                    Version = version,
                    OrderStatus = SalesOrderStatusEnum.已关闭,
                    UpdatedDate = updatedDate
                },
                transaction: Transaction);

            if (headerRows == 0)
                return false;

            await Connection.ExecuteAsync(
                deleteLinesSql,
                new
                {
                    OrderId = orderId,
                    UpdatedDate = updatedDate
                },
                transaction: Transaction);

            return true;
        }
    }
}

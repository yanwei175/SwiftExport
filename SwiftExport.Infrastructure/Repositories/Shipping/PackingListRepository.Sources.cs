using Dapper;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;

namespace SwiftExport.Infrastructure.Repositories.Shipping;

public sealed partial class PackingListRepository
{
    /// <summary>共用正式交付投影；售后补发免物料和 Target 准入，普通来源仍需执行关系，在计数和分页之前过滤。</summary>
    public async Task<PagedResponseBase<SourceDemandDeliveryItemDto>> SearchSourcesAsync(
        PackingListSourceSearchRequest request, CancellationToken cancellationToken = default)
    {
        var parameters = new DynamicParameters(new
        {
            request.CustomerId, Posted = PackingListPostingStatusEnum.Posted,
            Active = GrossDemandLifecycleStatusEnum.Active, MaterialActive = NetDemandLifecycleStatusEnum.Active,
            Skip = (request.PageIndex - 1) * request.PageSize, Take = request.PageSize
        });
        var sql = BuildSourceSearchSql(request, parameters);
        using var reader = await context.Session.Connection.QueryMultipleAsync(Command(sql, parameters, cancellationToken));
        return new() { TotalCount = await reader.ReadSingleAsync<int>(),
            Items = (await reader.ReadAsync<SourceDemandDeliveryItemDto>()).ToList(),
            PageIndex = request.PageIndex, PageSize = request.PageSize };
    }

    /// <summary>用户内容全部参数化；关键词之间 OR，单行关键词与批量集合亦合并为同一组。</summary>
    internal static string BuildSourceSearchSql(PackingListSourceSearchRequest request, DynamicParameters parameters)
    {
        parameters.Add("AfterSalesSourceType", DocumentTypeStaticsEnum.ComplaintReplenishment);
        var words = (request.Keywords ?? []).Append(request.Keyword).Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (words.Length > 201 || words.Any(x => x.Length > 200))
            throw new ArgumentException("搜索关键词超出数量或长度限制。", nameof(request));
        var matches = new List<string>();
        for (var i = 0; i < words.Length; i++)
        {
            var name = "Keyword" + i;
            parameters.Add(name, Like(words[i]));
            matches.Add($"(s.SourceNo LIKE @{name} ESCAPE '~' OR s.Sku LIKE @{name} ESCAPE '~' OR s.LinkSku LIKE @{name} ESCAPE '~' OR s.CustomerSku LIKE @{name} ESCAPE '~' OR s.Description LIKE @{name} ESCAPE '~' OR s.Color LIKE @{name} ESCAPE '~')");
        }
        var keywordFilter = matches.Count == 0 ? "" : " AND (" + string.Join(" OR ", matches) + ")";
        const string columns = "s.GrossDemandId,s.DemandVersion,s.SourceType,s.SourceUid,s.SourceNo,s.SourceLineId,s.SourceLineNumber,s.SourceVersion,s.CustomerId,s.CustomerCode,s.CustomerName,s.ProductId,s.IsNonStandard,s.Sku,s.LinkSku,s.CustomerSku,s.Description,s.Color,s.Specification,s.ImagePath,s.Unit,s.DemandQty,s.DeliveredQty,s.RemainingQty,s.LifecycleStatus,s.IsActive,s.RequiredDate";
        // 售后均为供应商直发补发，仅豁免执行关系；客户、活动状态和剩余量仍是共同条件。
        // 普通来源保持原未释放数量口径，不额外过滤 LinkStatus、TargetType 或采购确认状态。
        return SourceDeliveryQueryRepository.Projection(false) + $"""
            SELECT {columns} INTO #PackingListSources FROM SourceDelivery s
            WHERE s.IsActive=1 AND s.CustomerId=@CustomerId AND s.RemainingQty>0
              AND (s.SourceType=@AfterSalesSourceType OR EXISTS (
                SELECT 1 FROM dbo.NetDemand n
                INNER JOIN dbo.DemandExecutionLink target ON target.NetDemandId=n.Uid
                WHERE n.GrossDemandId=s.GrossDemandId AND n.IsDeleted=0 AND n.LifecycleStatus=@MaterialActive
                  AND target.IsDeleted=0 AND target.AllocatedQty>target.ReleasedQty
              )){keywordFilter};
            SELECT COUNT(1) FROM #PackingListSources;
            SELECT {columns} FROM #PackingListSources s
            ORDER BY s.SourceNo,s.SourceType,ISNULL(s.SourceLineNumber,2147483647),s.GrossDemandId
            OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY;
            DROP TABLE #PackingListSources;
            """;
    }
}

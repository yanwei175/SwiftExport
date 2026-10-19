using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.OrderTracking.Summary;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.OrderTracking.Summary;
namespace SwiftExport.Application.Services.OrderTracking.Summary;

/// <summary>按维度读取有权摘要，局部失败保留基础事实；不创建领域状态或写事务。</summary>
public sealed class OrderTrackingSummaryService(ServiceBaseContext context,IOrderTrackingSummaryRepository repository,
    ICurrentUser user,IPermissionEvaluationService permissions):IOrderTrackingSummaryService
{
    /// <summary>批量统计来源活动需求；收货另按来源采购行去重，禁止累加子需求共享行。</summary>
    public async Task<ServiceResult<OrderTrackingSourceSummaryBatchDto>> GetSourceSummariesAsync(OrderTrackingSourceSummaryRequest request,CancellationToken cancellationToken=default)
    {
        if(request?.Sources is null||request.Sources.Count>200||request.Sources.Any(s=>s is null||!Valid(s.SourceType,s.SourceGroupId)))
            return ServiceResult<OrderTrackingSourceSummaryBatchDto>.Fail(ServiceErrorCodes.Validation,"来源身份无效或超过200个。");
        if(!await Allowed(PermissionKeys.OrderTrackingView,cancellationToken))
            return ServiceResult<OrderTrackingSourceSummaryBatchDto>.Fail(ServiceErrorCodes.Forbidden,"没有订单跟踪查看权限。");
        var keys=request.Sources.DistinctBy(s=>(s.SourceType,s.SourceGroupId)).ToArray();
        var date=OrderTrackingBusinessDate.Today();
        try
        {
            var (rows,states)=await LoadAsync(keys,date,cancellationToken);
            var sourceReceipts=new List<OrderTrackingSourceSummaryDto>();
            if(states["Receipt"]=="Readable"&&rows.Count>0)
            {
                try {sourceReceipts=await repository.ReadSourceReceiptsAsync(keys,date,cancellationToken);}
                catch(OperationCanceledException){throw;}
                catch(Exception ex){states["Receipt"]="Failed";Log(ex,"Receipt");}
            }
            var result=new OrderTrackingSourceSummaryBatchDto{BusinessDate=date,ReadAt=DateTimeOffset.UtcNow};
            foreach(var group in rows.GroupBy(r=>(r.SourceType,r.SourceGroupId)))
            {
                var active=group.Where(r=>r.IsActive).ToArray();
                var item=new OrderTrackingSourceSummaryDto{SourceType=group.Key.SourceType,SourceGroupId=group.Key.SourceGroupId,
                    ActiveDemandCount=active.Length,DeliveryAvailability=states["Delivery"],DeliveryMessage=states["Delivery"]=="Failed"?"此区域摘要读取失败，请重试":null,
MaterialAvailability=states["Material"],MaterialMessage=states["Material"]=="Failed"?"此区域摘要读取失败，请重试":null,
PurchaseAvailability=states["Purchase"],PurchaseMessage=states["Purchase"]=="Failed"?"此区域摘要读取失败，请重试":null,
ReceiptAvailability=states["Receipt"],ReceiptMessage=states["Receipt"]=="Failed"?"此区域摘要读取失败，请重试":null,
                    AttentionCodes=active.SelectMany(r=>r.AttentionCodes).Distinct().ToList()};
                if(states["Delivery"]=="Readable"){
                    item.DeliveryCompletedDemandCount=active.Count(r=>r.DeliveryStatus=="Delivered");
                    item.DeliveryOutstandingDemandCount=active.Count(r=>r.RemainingQty>0);
                    item.DeliveryReviewDemandCount=active.Count(r=>r.DeliveryStatus=="Review");
                    item.OverdueDemandCount=active.Count(r=>r.IsOverdue==true);
                    item.MaxOverdueDays=active.Where(r=>r.IsOverdue==true).Select(r=>r.OverdueDays??0).DefaultIfEmpty(0).Max();
                }
                if(states["Material"]=="Readable"){
                    item.MaterialEstablishedDemandCount=active.Count(r=>r.ActiveMaterialCount>0);
                    item.NoActiveMaterialDemandCount=active.Count(r=>r.ActiveMaterialCount==0);
                    item.MaterialReviewDemandCount=active.Count(r=>r.MaterialReviewCount>0);
                }
                if(states["Purchase"]=="Readable"){
                    item.PurchaseApplicableDemandCount=active.Count(r=>r.ApplicableMaterialCount>0);
                    item.PurchaseShortfallDemandCount=active.Count(r=>r.PurchaseShortfallMaterialCount>0);
                    item.PurchaseExcessDemandCount=active.Count(r=>r.PurchaseExcessMaterialCount>0);
                    item.PurchaseReviewDemandCount=active.Count(r=>r.PurchaseReviewMaterialCount>0);
                }
                if(states["Receipt"]=="Readable"){
                    var receipt=sourceReceipts.SingleOrDefault(r=>r.SourceType==item.SourceType&&r.SourceGroupId==item.SourceGroupId);
                    if(receipt is null){item.ReceiptAvailability="Failed";item.ReceiptMessage="收货摘要缺少来源，请重试";}
                    else {
                        item.ReceiptPurchaseLineCount=receipt.ReceiptPurchaseLineCount;item.ReceiptCompletedLineCount=receipt.ReceiptCompletedLineCount;
                        item.ReceiptOutstandingLineCount=receipt.ReceiptOutstandingLineCount;item.ReceiptReviewLineCount=receipt.ReceiptReviewLineCount;
                        item.ReceiptExcessLineCount=receipt.ReceiptExcessLineCount;item.SharedPurchaseLineCount=receipt.SharedPurchaseLineCount;
                    }
                }
                if(item.ReceiptAvailability!="Readable")item.AttentionCodes.RemoveAll(c=>c.StartsWith("Receipt",StringComparison.Ordinal));
                item.ReviewReason=string.Join("；",active.Where(r=>r.HasSourceReview).Select(_=>"来源身份或快照待核实").Distinct());
                result.Items.Add(item);
            }
            return ServiceResult<OrderTrackingSourceSummaryBatchDto>.Ok(result);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){Log(ex,"Base");return ServiceResult<OrderTrackingSourceSummaryBatchDto>.Fail(ServiceErrorCodes.ServiceUnavailable,"来源摘要读取失败，请重试。");}
    }

    /// <summary>读取全部需求摘要；没有记录返回空集合，读取失败不可伪装成空集合。</summary>
    public async Task<ServiceResult<OrderTrackingSourceDemandSummariesDto>> GetSourceDemandSummariesAsync(string sourceType,Guid sourceGroupId,CancellationToken cancellationToken=default)
    {
        if(!Valid(sourceType,sourceGroupId))return ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail(ServiceErrorCodes.Validation,"来源身份无效。");
        if(!await Allowed(PermissionKeys.OrderTrackingView,cancellationToken))return ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail(ServiceErrorCodes.Forbidden,"没有订单跟踪查看权限。");
        var date=OrderTrackingBusinessDate.Today();
        try{
            var (rows,_)=await LoadAsync([new(){SourceType=sourceType,SourceGroupId=sourceGroupId}],date,cancellationToken);
            return ServiceResult<OrderTrackingSourceDemandSummariesDto>.Ok(new(){SourceType=sourceType,SourceGroupId=sourceGroupId,BusinessDate=date,ReadAt=DateTimeOffset.UtcNow,Items=rows});
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex){Log(ex,"Base");return ServiceResult<OrderTrackingSourceDemandSummariesDto>.Fail(ServiceErrorCodes.ServiceUnavailable,"需求摘要读取失败，请重试。");}
    }

    /// <summary>固定数量的维度读取；同一请求会话顺序使用，既不并发共用连接也不按需求发请求。</summary>
    private async Task<(List<OrderTrackingDemandSummaryDto> Rows,Dictionary<string,string> States)> LoadAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> keys,DateTime date,CancellationToken token)
    {
        var states=new Dictionary<string,string>();
        var material=await Allowed(PermissionKeys.DemandPlanningMaterialView,token);
        var purchase=material&&await Allowed(PermissionKeys.ProcurementOrderRead,token);
        var access=new Dictionary<string,bool>{
            ["Delivery"]=await Allowed(PermissionKeys.ShippingPackingListView,token),["Material"]=material,
            ["Purchase"]=purchase,["Receipt"]=purchase&&await Allowed(PermissionKeys.InventoryInboundView,token)};
        var rows=keys.Count==0?[]:await repository.ReadDimensionAsync(keys,"Base",date,token);
        foreach(var dimension in access.Keys)
        {
            states[dimension]=access[dimension]?"Readable":"Forbidden";
            foreach(var row in rows)Apply(row,null,dimension,states[dimension],null);
            if(!access[dimension]||rows.Count==0)continue;
            try{
                var values=(await repository.ReadDimensionAsync(keys,dimension,date,token)).ToDictionary(r=>r.GrossDemandId);
                if(rows.Any(row=>!values.TryGetValue(row.GrossDemandId,out var value)||value.DemandVersion!=row.DemandVersion||value.Unit!=row.Unit))throw new InvalidOperationException("摘要维度读取范围发生变化。");
                foreach(var row in rows)Apply(row,values[row.GrossDemandId],dimension,"Readable",null);
            }
            catch(OperationCanceledException){throw;}
            catch(Exception ex){
                states[dimension]="Failed";Log(ex,dimension);
                foreach(var row in rows)Apply(row,null,dimension,"Failed","此维度读取失败，请重试。");
            }
        }
        foreach(var row in rows)AddAttention(row);
        return (rows,states);
    }

    /// <summary>只复制当前维度字段，不能让部分投影覆盖其他维度、身份、单位或版本。</summary>
    private static void Apply(OrderTrackingDemandSummaryDto target,OrderTrackingDemandSummaryDto? value,string dimension,string availability,string? message)
    {
        switch(dimension){
            case "Delivery":
                target.DeliveredQty=value?.DeliveredQty;
                target.RemainingQty=value?.RemainingQty;
                target.IsOverdue=value?.IsOverdue;
                target.OverdueDays=value?.OverdueDays;
                target.DeliveryStatus=value?.DeliveryStatus;
                target.DeliveryAvailability=availability;target.DeliveryMessage=message;break;
            case "Material":
                target.ActiveMaterialCount=value?.ActiveMaterialCount;
                target.HistoricalMaterialCount=value?.HistoricalMaterialCount;
                target.MaterialReviewCount=value?.MaterialReviewCount;
                target.MaterialAvailability=availability;target.MaterialMessage=message;break;
            case "Purchase":
                target.ApplicableMaterialCount=value?.ApplicableMaterialCount;
                target.PurchaseShortfallMaterialCount=value?.PurchaseShortfallMaterialCount;
                target.PurchaseExcessMaterialCount=value?.PurchaseExcessMaterialCount;
                target.PurchaseReviewMaterialCount=value?.PurchaseReviewMaterialCount;
                target.PurchaseStatus=value?.PurchaseStatus;
                target.PurchaseAvailability=availability;target.PurchaseMessage=message;break;
            case "Receipt":
                target.ReceiptPurchaseLineCount=value?.ReceiptPurchaseLineCount;
                target.ReceiptCompletedLineCount=value?.ReceiptCompletedLineCount;
                target.ReceiptOutstandingLineCount=value?.ReceiptOutstandingLineCount;
                target.ReceiptReviewLineCount=value?.ReceiptReviewLineCount;
                target.ReceiptExcessLineCount=value?.ReceiptExcessLineCount;
                target.SharedPurchaseLineCount=value?.SharedPurchaseLineCount;
                target.ReceiptStatus=value?.ReceiptStatus;
                target.ReceiptAvailability=availability;target.ReceiptMessage=message;break;
        }
    }

    /// <summary>服务端摘要关注码由相同事实判定；非活动需求保留事实但不冒充当前执行异常。</summary>
    private static void AddAttention(OrderTrackingDemandSummaryDto row)
    {
        row.AttentionCodes=[];
        if(!row.IsActive)return;
        if(row.HasSourceReview)row.AttentionCodes.Add("SourceReview");
        if(row.MissingRequiredDate)row.AttentionCodes.Add("MissingRequiredDate");
        if(row.IsOverdue==true)row.AttentionCodes.Add("Overdue");
        if(row.DeliveryAvailability=="Readable"&&row.DeliveryStatus=="Review")row.AttentionCodes.Add("DeliveryReview");
        if(row.ActiveMaterialCount==0)row.AttentionCodes.Add("NoActiveMaterial");
        if(row.PurchaseShortfallMaterialCount>0)row.AttentionCodes.Add("PurchaseShortfall");
        if(row.PurchaseExcessMaterialCount>0)row.AttentionCodes.Add("PurchaseExcess");
        if(row.PurchaseReviewMaterialCount>0)row.AttentionCodes.Add("PurchaseReview");
        if(row.ReceiptOutstandingLineCount>0)row.AttentionCodes.Add("ReceiptOutstanding");
        if(row.ReceiptExcessLineCount>0)row.AttentionCodes.Add("ReceiptExcess");
        if(row.ReceiptReviewLineCount>0)row.AttentionCodes.Add("ReceiptReview");
        row.ReviewReason=row.HasSourceReview?"来源身份或快照待核实":null;
    }
    /// <summary>账号与权限均由现有权限投影决定，不接受客户端宣称有权。</summary>
    private Task<bool> Allowed(string key,CancellationToken token)=>user.IsAuthenticated&&user.UserId is Guid id&&id!=Guid.Empty
        ?permissions.HasPermissionAsync(id,key,token):Task.FromResult(false);
    private static bool Valid(string type,Guid id)=>type is "SO" or "CR" or "MD"&&id!=Guid.Empty;
    /// <summary>记录维度故障，不把数据库内部详情暴露给客户端。</summary>
    private void Log(Exception ex,string dimension)=>context.LoggerFactory.CreateLogger<OrderTrackingSummaryService>().LogError(ex,"订单跟踪摘要维度 {Dimension} 读取失败",dimension);
}

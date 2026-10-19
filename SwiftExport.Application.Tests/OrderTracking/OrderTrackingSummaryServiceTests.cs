using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.OrderTracking.Summary;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.OrderTracking.Summary;
namespace SwiftExport.Application.Tests.OrderTracking;
/// <summary>验证摘要权限不触库、失败隔离、版本变化及来源去重汇总。</summary>
[TestClass]
public sealed class OrderTrackingSummaryServiceTests
{
    /// <summary>只有基础权限时，不调用任何下游仓储且不暴露其关注项。</summary>
    [TestMethod]
    public async Task BasePermission_DoesNotReadOrLeakDownstream()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var p=new Permissions{Keys=[PermissionKeys.OrderTrackingView]};
        var result=await Service(f,repo,p).GetSourceDemandSummariesAsync("SO",repo.Source);
        Assert.IsTrue(result.Success);CollectionAssert.AreEqual(new[]{"Base"},repo.Calls.ToArray());
        var row=result.Data.Items.Single();Assert.AreEqual("Forbidden",row.DeliveryAvailability);Assert.IsNull(row.DeliveredQty);
        Assert.AreEqual("Forbidden",row.PurchaseAvailability);Assert.IsNull(row.ApplicableMaterialCount);
        Assert.IsFalse(row.AttentionCodes.Contains("PurchaseShortfall"));
    }
    /// <summary>缺物料权限不能通过采购或收货摘要读取当前物料关系。</summary>
    [TestMethod]
    public async Task MissingMaterialPermission_BlocksPurchaseAndReceipt()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var p=All();p.Keys.Remove(PermissionKeys.DemandPlanningMaterialView);
        var result=await Service(f,repo,p).GetSourceDemandSummariesAsync("SO",repo.Source);
        CollectionAssert.AreEqual(new[]{"Base","Delivery"},repo.Calls.ToArray());
        Assert.AreEqual("Forbidden",result.Data.Items.Single().ReceiptAvailability);
    }
    /// <summary>单区域失败不清空基础、其他摘要或把失败变成零。</summary>
    [TestMethod]
    public async Task DimensionFailure_PreservesOtherFacts()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository{Fail="Purchase"};
        var result=await Service(f,repo,All()).GetSourceDemandSummariesAsync("SO",repo.Source);
        var row=result.Data.Items.Single();Assert.AreEqual("Failed",row.PurchaseAvailability);Assert.IsNull(row.ApplicableMaterialCount);
        Assert.AreEqual("Readable",row.DeliveryAvailability);Assert.AreEqual(3m,row.DeliveredQty);
        Assert.AreEqual("Readable",row.ReceiptAvailability);Assert.AreEqual(1,row.ReceiptOutstandingLineCount);
    }
    /// <summary>独立查询之间需求版本变化时不混用交付数量和旧单位。</summary>
    [TestMethod]
    public async Task VersionChange_MarksAffectedDimensionFailed()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository{ChangedVersion=true};
        var result=await Service(f,repo,All()).GetSourceDemandSummariesAsync("SO",repo.Source);
        Assert.AreEqual("Failed",result.Data.Items.Single().DeliveryAvailability);Assert.IsNull(result.Data.Items.Single().DeliveredQty);
    }
    /// <summary>非活动行保留但不进入活动摘要；来源收货必须消费独立去重结果。</summary>
    [TestMethod]
    public async Task SourceSummary_UsesActiveRowsAndDistinctPurchaseLines()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository{Rows=3};
        var result=await Service(f,repo,All()).GetSourceSummariesAsync(new(){Sources=[new(){SourceType="SO",SourceGroupId=repo.Source}]});
        var row=result.Data.Items.Single();Assert.AreEqual(2,row.ActiveDemandCount);
        Assert.AreEqual(1,row.ReceiptPurchaseLineCount);Assert.AreEqual(1,row.ReceiptOutstandingLineCount);
        Assert.AreEqual(2,row.PurchaseShortfallDemandCount);Assert.AreEqual(6,repo.Calls.Count);
    }
    /// <summary>取消原样传播；无基础权限、空身份、过大请求在仓储前拒绝。</summary>
    [TestMethod]
    public async Task ValidationAndCancellation_DoNotHideFailure()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var service=Service(f,repo,All());
        Assert.IsFalse((await service.GetSourceDemandSummariesAsync("ZZ",repo.Source)).Success);
        Assert.IsFalse((await service.GetSourceSummariesAsync(new(){Sources=Enumerable.Range(0,201).Select(_=>new OrderTrackingSourceKeyDto{SourceType="SO",SourceGroupId=Guid.NewGuid()}).ToList()})).Success);
        Assert.HasCount(0,repo.Calls);
        repo.Fail="Cancel";await Assert.ThrowsAsync<OperationCanceledException>(()=>service.GetSourceDemandSummariesAsync("SO",repo.Source));
        repo.Calls.Clear();Assert.IsFalse((await Service(f,repo,new()).GetSourceDemandSummariesAsync("SO",repo.Source)).Success);Assert.HasCount(0,repo.Calls);
    }

    internal static OrderTrackingSummaryService Service(OrderTrackingReadFixture f,IOrderTrackingSummaryRepository repo,Permissions permissions)
        =>new(new ServiceBaseContext(f.Session,NullLoggerFactory.Instance),repo,new User(),permissions);
    internal static Permissions All()=>new(){Keys=[PermissionKeys.OrderTrackingView,PermissionKeys.ShippingPackingListView,PermissionKeys.DemandPlanningMaterialView,PermissionKeys.ProcurementOrderRead,PermissionKeys.InventoryInboundView]};
    /// <summary>测试有效登录身份。</summary>
    internal sealed class User:ICurrentUser
    {
        public string UserName=>"summary-test";
        public Guid? UserId=>Guid.Parse("9a8d0399-1946-4da5-9a21-582b0762bc23");
        public bool IsAuthenticated=>true;
    }
    /// <summary>显式权限集合，避免测试隐含管理员越权。</summary>
    internal sealed class Permissions:IPermissionEvaluationService
    {
        internal HashSet<string> Keys=[];
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid id,CancellationToken token=default)=>throw new NotSupportedException();
        public Task<bool> HasPermissionAsync(Guid id,string key,CancellationToken token=default)=>Task.FromResult(Keys.Contains(key));
    }
    /// <summary>按维度返回独立投影，记录是否触及无权数据。</summary>
    private sealed class Repository:IOrderTrackingSummaryRepository
    {
        internal Guid Source=Guid.NewGuid();internal string? Fail;internal bool ChangedVersion;internal int Rows=1;
        internal List<string> Calls=[];
        private readonly Guid[] ids=[Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid()];
        public Task<List<OrderTrackingDemandSummaryDto>> ReadDimensionAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,string dimension,DateTime date,CancellationToken token=default)
        {
            Calls.Add(dimension);if(Fail=="Cancel")throw new OperationCanceledException();if(Fail==dimension)throw new InvalidOperationException("controlled");
            return Task.FromResult(Enumerable.Range(0,Rows).Select(i=>new OrderTrackingDemandSummaryDto{
                GrossDemandId=ids[i],SourceType="SO",SourceGroupId=Source,DemandVersion=ChangedVersion&&dimension=="Delivery"?2:1,
                Unit="PCS",IsActive=i!=2,DeliveredQty=3m,RemainingQty=7m,DeliveryStatus="Partial",IsOverdue=false,OverdueDays=0,
                ActiveMaterialCount=2,HistoricalMaterialCount=1,MaterialReviewCount=0,ApplicableMaterialCount=2,
                PurchaseShortfallMaterialCount=1,PurchaseExcessMaterialCount=0,PurchaseReviewMaterialCount=0,PurchaseStatus="Partial",
                ReceiptPurchaseLineCount=1,ReceiptOutstandingLineCount=1,ReceiptCompletedLineCount=0,ReceiptReviewLineCount=0,
                ReceiptExcessLineCount=0,SharedPurchaseLineCount=1,ReceiptStatus="Outstanding"}).ToList());
        }
        public Task<List<OrderTrackingSourceSummaryDto>> ReadSourceReceiptsAsync(IReadOnlyCollection<OrderTrackingSourceKeyDto> sources,DateTime date,CancellationToken token=default)
        {
            Calls.Add("SourceReceipt");return Task.FromResult(new List<OrderTrackingSourceSummaryDto>{new(){SourceType="SO",SourceGroupId=Source,
                ReceiptPurchaseLineCount=1,ReceiptCompletedLineCount=0,ReceiptOutstandingLineCount=1,ReceiptReviewLineCount=0,ReceiptExcessLineCount=0,SharedPurchaseLineCount=1}});
        }
    }
}

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.OrderTracking.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.OrderTracking.PurchaseOrders;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>采购单跟踪维度权限在读取前拒绝，缺权不可回落成零事实或忽略筛选。</summary>
[TestClass]
public sealed class PurchaseOrderTrackingServiceTests
{
    /// <summary>基础权限必须同时具备，缺任意一个都不能触及仓储。</summary>
    [TestMethod]
    public async Task MissingBasePermissionDoesNotRead()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();
        foreach(var keys in new HashSet<string>[]{[],[PermissionKeys.OrderTrackingView],[PermissionKeys.ProcurementOrderRead]})
        {
            var service=Service(f,repo,keys);
            Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.SearchPurchaseOrdersAsync(new())).Error.Code);
            Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderLinesAsync(Guid.NewGuid())).Error.Code);
            Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderReceiptsAsync(Guid.NewGuid())).Error.Code);
            Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderReportsAsync(Guid.NewGuid())).Error.Code);
        }
        Assert.HasCount(0,repo.Calls);
    }

    /// <summary>基础权限允许订单事实，两个维度的实际读取资格均明确关闭。</summary>
    [TestMethod]
    public async Task BaseReaderPassesDisabledDimensionsAndKeepsFullLines()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var service=Service(f,repo,Base());
        var page=await service.SearchPurchaseOrdersAsync(new(){PageSize=1});Assert.IsTrue(page.Success);Assert.IsFalse(repo.Progress);Assert.IsFalse(repo.Receipts);
        var lines=await service.GetPurchaseOrderLinesAsync(Guid.NewGuid());Assert.IsTrue(lines.Success);Assert.HasCount(206,lines.Data);
        Assert.IsFalse(repo.Progress);Assert.IsFalse(repo.Receipts);
    }

    /// <summary>维度条件缺权不能静默忽略，拒绝时连头存在性也不读取。</summary>
    [TestMethod]
    public async Task UnauthorizedFiltersAndEvidenceAreRejectedBeforeRead()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var service=Service(f,repo,Base());
        foreach(var request in new PurchaseOrderTrackingSearchRequest[]{new(){ReadyStatuses=[PurchaseReadyStatusEnum.NotReported]},new(){ReceiptStatuses=[PurchaseReceiptStatusEnum.NotReceived]},new(){DueView=PurchaseDueViewEnum.OverdueNotReady},new(){ExpectedCompletionDateFrom=DateTime.Today},new(){ExpectedCompletionDateTo=DateTime.Today}})
            Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.SearchPurchaseOrdersAsync(request)).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderReceiptsAsync(Guid.NewGuid())).Error.Code);
        Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderReportsAsync(Guid.NewGuid())).Error.Code);
        Assert.HasCount(0,repo.Calls);
    }

    /// <summary>单维度授予只开启对应读取，完整反馈与正式收货保持独立入口。</summary>
    [TestMethod]
    public async Task EachDimensionRequiresItsOwnPermission()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var keys=Base();keys.Add(PermissionKeys.ProcurementProgressView);
        var service=Service(f,repo,keys);Assert.IsTrue((await service.SearchPurchaseOrdersAsync(new(){ReadyStatuses=[PurchaseReadyStatusEnum.NotReported]})).Success);
        Assert.IsTrue(repo.Progress);Assert.IsFalse(repo.Receipts);Assert.IsTrue((await service.GetPurchaseOrderReportsAsync(Guid.NewGuid())).Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderReceiptsAsync(Guid.NewGuid())).Error.Code);
        keys.Remove(PermissionKeys.ProcurementProgressView);keys.Add(PermissionKeys.InventoryInboundView);
        Assert.IsTrue((await service.SearchPurchaseOrdersAsync(new(){ReceiptStatuses=[PurchaseReceiptStatusEnum.NotReceived]})).Success);
        Assert.IsFalse(repo.Progress);Assert.IsTrue(repo.Receipts);Assert.IsTrue((await service.GetPurchaseOrderReceiptsAsync(Guid.NewGuid())).Success);
        Assert.AreEqual(ServiceErrorCodes.Forbidden,(await service.GetPurchaseOrderReportsAsync(Guid.NewGuid())).Error.Code);
    }

    /// <summary>错误身份、无记录与技术失败都不能变成成功空列表；取消继续传播。</summary>
    [TestMethod]
    public async Task MissingInvalidFailedAndCancelledRemainDistinct()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var service=Service(f,repo,Base());
        Assert.AreEqual(ServiceErrorCodes.Validation,(await service.GetPurchaseOrderLinesAsync(Guid.Empty)).Error.Code);Assert.HasCount(0,repo.Calls);
        repo.Exists=false;Assert.AreEqual(ServiceErrorCodes.NotFound,(await service.GetPurchaseOrderLinesAsync(Guid.NewGuid())).Error.Code);
        repo.Fail=true;Assert.AreEqual(ServiceErrorCodes.ServiceUnavailable,(await service.SearchPurchaseOrdersAsync(new())).Error.Code);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.SearchPurchaseOrdersAsync(new(),new CancellationToken(true)));
    }

    /// <summary>页码、状态、日期区间和多选身份在SQL前校验。</summary>
    [TestMethod]
    public async Task InvalidSearchNeverReachesRepository()
    {
        using var f=new OrderTrackingReadFixture();var repo=new Repository();var service=Service(f,repo,Base());
        foreach(var request in new PurchaseOrderTrackingSearchRequest[]{new(){PageSize=201},new(){PageIndex=0},new(){CustomerCodes=null!},new(){SupplierIds=[Guid.Empty]},new(){DueView=(PurchaseDueViewEnum)999},new(){OrderDateFrom=DateTime.Today.AddDays(1),OrderDateTo=DateTime.Today}})
            Assert.AreEqual(ServiceErrorCodes.Validation,(await service.SearchPurchaseOrdersAsync(request)).Error.Code);
        Assert.HasCount(0,repo.Calls);
    }

    /// <summary>四个公开Refit签名、HTTP方法及参数与MVC边界完全一致。</summary>
    [TestMethod]
    public void ContractRoutesMatchController()
    {
        var controller=typeof(SwiftExport.WebApi.Controllers.OrderTracking.PurchaseOrderTrackingController);
        foreach(var method in typeof(SwiftExport.Application.Interfaces.OrderTracking.PurchaseOrders.IPurchaseOrderTrackingService).GetMethods())
        {
            var api=typeof(SwiftExport.Contracts.Apis.OrderTracking.IOrderTrackingApi).GetMethod(method.Name)!;
            var action=controller.GetMethod(method.Name)!;
            Assert.AreEqual(api.ReturnType,action.ReturnType);CollectionAssert.AreEqual(api.GetParameters().Select(p=>p.ParameterType).ToArray(),action.GetParameters().Select(p=>p.ParameterType).ToArray());
            var route=action.GetCustomAttributes(typeof(Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute),false).Cast<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Single();
            var declared=api.GetCustomAttributesData().Single(a=>a.AttributeType==typeof(Refit.GetAttribute)||a.AttributeType==typeof(Refit.PostAttribute));
            Assert.AreEqual(route.HttpMethods.Single(),declared.AttributeType.Name=="GetAttribute"?"GET":"POST");
            Assert.AreEqual((string)declared.ConstructorArguments[0].Value!,"/purchase-orders/"+route.Template!.Replace(":guid}","}"));
        }
    }

    /// <summary>构造显式账号和权限，不接网络或管理员后门。</summary>
    private static PurchaseOrderTrackingService Service(OrderTrackingReadFixture f,Repository repository,HashSet<string> keys)
        =>new(new ServiceBaseContext(f.Session,NullLoggerFactory.Instance),new OrderTrackingSummaryServiceTests.User(),new OrderTrackingSummaryServiceTests.Permissions{Keys=keys},repository);
    /// <summary>仅采购跟踪基础权限。</summary>
    private static HashSet<string> Base()=>[PermissionKeys.OrderTrackingView,PermissionKeys.ProcurementOrderRead];
    /// <summary>只记录编排与权限参数，不模拟SQL状态算法。</summary>
    private sealed class Repository:IPurchaseOrderTrackingRepository
    {
        internal List<string> Calls=[];internal bool Progress,Receipts,Exists=true,Fail;
        /// <inheritdoc/>
        public Task<PagedResponseBase<PurchaseOrderTrackingItemDto>> SearchAsync(PurchaseOrderTrackingSearchRequest request,bool progress,bool receipts,CancellationToken ct=default)
        {Calls.Add("Search");Progress=progress;Receipts=receipts;if(Fail)throw new InvalidOperationException();return Task.FromResult(new PagedResponseBase<PurchaseOrderTrackingItemDto>());}
        /// <inheritdoc/>
        public Task<IReadOnlyList<PurchaseOrderTrackingLineDto>> GetLinesAsync(Guid id,bool progress,bool receipts,CancellationToken ct=default)
        {Calls.Add("Lines");Progress=progress;Receipts=receipts;return Task.FromResult<IReadOnlyList<PurchaseOrderTrackingLineDto>>(Enumerable.Range(1,206).Select(n=>new PurchaseOrderTrackingLineDto{LineNumber=n}).ToList());}
        /// <inheritdoc/>
        public Task<bool> ExistsAsync(Guid id,CancellationToken ct=default){Calls.Add("Exists");return Task.FromResult(Exists);}
        /// <inheritdoc/>
        public Task<PurchaseOrderTrackingReceiptsDto> GetReceiptsAsync(Guid id,CancellationToken ct=default){Calls.Add("Receipts");return Task.FromResult(new PurchaseOrderTrackingReceiptsDto{PurchaseOrderId=id});}
        /// <inheritdoc/>
        public Task<List<SupplierProgressReportDto>> GetReportsAsync(Guid id,CancellationToken ct=default){Calls.Add("Reports");return Task.FromResult(new List<SupplierProgressReportDto>());}
    }
}

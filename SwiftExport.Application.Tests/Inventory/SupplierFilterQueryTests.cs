using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Inventory.Inbounds;
using SwiftExport.Application.Services.Inventory.Query;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Core.Interfaces.Inventory.Query;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Data;
using SwiftExport.WebApi.Controllers.Inventory.Inbounds;
using SwiftExport.WebApi.Controllers.Inventory.Query;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>验证入库和库存供应商筛选的独立授权、完整分页与失败边界，不依赖全量缓存或业务记录。</summary>
[TestClass]
public sealed class SupplierFilterQueryTests
{
    /// <summary>只授予所属页面查看权限即可读取；匿名、无权限或其他模块权限不能进入候选仓储。</summary>
    [TestMethod]
    [DataRow(true,true,"Inventory.Inbound.View",true)]
    [DataRow(false,true,"Inventory.Balance.View",true)]
    [DataRow(true,false,"Inventory.Inbound.View",false)]
    [DataRow(false,false,"Inventory.Balance.View",false)]
    [DataRow(true,true,"Inventory.Balance.View",false)]
    [DataRow(false,true,"Inventory.Inbound.View",false)]
    [DataRow(true,true,"Suppliers.Supplier.View",false)]
    [DataRow(false,true,"Suppliers.Supplier.View",false)]
    [DataRow(true,true,"",false)] [DataRow(false,true,"",false)]
    public async Task CandidateReadRequiresOnlyItsOwnView(bool inbound,bool authenticated,string permission,bool accepted)
    {
        var f=new Fixture(authenticated,[permission]);using var cancellation=new CancellationTokenSource();
        var request=new PagedRequestBase{Keyword="  供应商  ",PageIndex=2,PageSize=200};
        var result=await f.CallAsync(inbound,request,cancellation.Token);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Reads);
        Assert.AreEqual("  供应商  ",request.Keyword);
        if(accepted)
        {
            Assert.AreSame(f.Page,result.Data);Assert.AreEqual(501,result.Data.TotalCount);
            Assert.AreEqual(2,f.Request!.PageIndex);Assert.AreEqual(200,f.Request.PageSize);
            Assert.AreEqual("供应商",f.Request.Keyword);Assert.AreEqual(cancellation.Token,f.Token);
        }
        else Assert.AreEqual(!authenticated&&inbound?ServiceErrorCodes.Unauthenticated:ServiceErrorCodes.Forbidden,result.Error.Code);
        if(authenticated)Assert.AreEqual(inbound?"Inventory.Inbound.View":"Inventory.Balance.View",f.Keys.Single());
        else Assert.IsEmpty(f.Keys);
    }

    /// <summary>两个入口均消费二百字符及二百条边界并拒绝零页码，不截断或改写原请求。</summary>
    [TestMethod]
    [DataRow(true,200,200,1,true)] [DataRow(false,200,200,1,true)]
    [DataRow(true,201,20,1,false)] [DataRow(false,201,20,1,false)]
    [DataRow(true,1,201,1,false)] [DataRow(false,1,201,1,false)]
    [DataRow(true,1,20,0,false)] [DataRow(false,1,20,0,false)]
    public async Task QueryLimitsRejectBeforeCandidateRead(bool inbound,int length,int pageSize,int pageIndex,bool accepted)
    {
        var f=new Fixture(true,["Inventory.Inbound.View","Inventory.Balance.View"]);
        var request=new PagedRequestBase{Keyword=new string('商',length),PageSize=pageSize,PageIndex=pageIndex};
        var result=await f.CallAsync(inbound,request,default);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Reads);Assert.AreEqual(length,request.Keyword.Length);
    }

    /// <summary>仓储取消沿 Controller 和 Service 原样上抛；技术失败保留失败回执而非成功空候选。</summary>
    [TestMethod,DataRow(true),DataRow(false)]
    public async Task CandidateCancellationAndFailureAreNotEmptySuccess(bool inbound)
    {
        var f=new Fixture(true,["Inventory.Inbound.View","Inventory.Balance.View"]);using var cancellation=new CancellationTokenSource();
        f.BeforeRead=cancellation.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.CallAsync(inbound,new(),cancellation.Token));
        Assert.AreEqual(cancellation.Token,f.Token);Assert.AreEqual(1,f.Reads);
        f.BeforeRead=null;f.FailRead=true;
        var failure=await f.CallAsync(inbound,new(),default);Assert.IsFalse(failure.Success);Assert.IsNotNull(failure.Error);
    }

    /// <summary>新接口使用 GET 查询绑定及窄 DTO，保持两个所属模块基路由，不暴露供应商管理字段。</summary>
    [TestMethod,DataRow(true),DataRow(false)]
    public void SupplierContractHasOnlyFilterFieldsAndMatchingQueryBinding(bool inbound)
    {
        CollectionAssert.AreEquivalent(new[]{"Uid","SupplierCode","SupplierName"},typeof(InventorySupplierLookupDto).GetProperties().Select(x=>x.Name).ToArray());
        var api=(inbound?typeof(IInboundSourceApi):typeof(IInventoryApi)).GetMethod("GetSupplierOptionsAsync")!;
        var action=(inbound?typeof(InboundSourcesController):typeof(InventoryController)).GetMethod("GetSupplierOptionsAsync")!;
        Assert.AreEqual("/suppliers/picker",api.GetCustomAttributes(typeof(GetAttribute),false).Cast<GetAttribute>().Single().Path);
        Assert.AreEqual("suppliers/picker",action.GetCustomAttributes(typeof(HttpGetAttribute),false).Cast<HttpGetAttribute>().Single().Template);
        Assert.IsTrue(api.GetParameters()[0].IsDefined(typeof(QueryAttribute),false));
        Assert.IsTrue(action.GetParameters()[0].IsDefined(typeof(FromQueryAttribute),false));
        Assert.AreEqual(typeof(Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>>),api.ReturnType);Assert.AreEqual(api.ReturnType,action.ReturnType);
    }

    /// <summary>所有无关库存、采购和仓库管理读取端口意外访问立即失败，权限检查只允许显式键。</summary>
    private sealed class Fixture
    {
        private readonly InboundSourceQueryService inbound;
        private readonly InventoryQueryService inventory;
        internal int Reads {get;private set;}
        internal List<string> Keys {get;}=[];
        internal PagedRequestBase? Request {get;private set;}
        internal CancellationToken Token {get;private set;}
        internal Action? BeforeRead {get;set;}
        internal bool FailRead {get;set;}
        internal PagedResponseBase<InventorySupplierLookupDto> Page {get;}=new(){PageIndex=2,PageSize=200,TotalCount=501,Items=[new(){Uid=Guid.NewGuid(),SupplierCode="S01",SupplierName="筛选供应商"}]};

        /// <summary>组装真实服务及 HTTP 转发，仅替换用户、精确权限和专用供应商读取端口。</summary>
        internal Fixture(bool authenticated,IReadOnlyCollection<string> granted)
        {
            var session=PurchaseDraftSourceOrchestrationTests.Proxy<IDbSession>.New((method,_)=>method.Name=="get_Errors"?PostgreSqlExceptionClassifier.Instance:throw new AssertFailedException(method.Name));
            var context=new ServiceBaseContext(session,NullLoggerFactory.Instance);
            var user=PurchaseDraftSourceOrchestrationTests.Proxy<ICurrentUser>.New((method,_)=>method.Name switch
            {"get_IsAuthenticated"=>authenticated,"get_UserId"=>(Guid?)Guid.Parse("d6bfbb81-2874-4113-9a57-501f43caf826"),_=>throw new AssertFailedException(method.Name)});
            var permissions=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>
            {Assert.AreEqual("HasPermissionAsync",method.Name);Keys.Add((string)args[1]!);return Task.FromResult(granted.Contains((string)args[1]!));});
            var warehouses=PurchaseDraftSourceOrchestrationTests.Proxy<IWarehouseQueryRepository>.New((method,args)=>
            {
                Assert.AreEqual("SearchSupplierFilterOptionsAsync",method.Name);Reads++;Request=(PagedRequestBase)args[0]!;Token=(CancellationToken)args[1]!;
                BeforeRead?.Invoke();if(FailRead)throw new InvalidOperationException("模拟供应商读取失败");
                return Token.IsCancellationRequested?Task.FromCanceled<PagedResponseBase<InventorySupplierLookupDto>>(Token):Task.FromResult(Page);
            });
            var sources=PurchaseDraftSourceOrchestrationTests.Proxy<IInboundSourceRepository>.New((method,_)=>throw new AssertFailedException(method.Name));
            var balances=PurchaseDraftSourceOrchestrationTests.Proxy<IInventoryQueryRepository>.New((method,_)=>throw new AssertFailedException(method.Name));
            inbound=new(sources,new InboundServiceSupport(user,permissions,NullLogger<InboundServiceSupport>.Instance,PostgreSqlExceptionClassifier.Instance),warehouses);
            inventory=new(context,balances,user,permissions,warehouses);
        }

        /// <summary>保留真实 Controller 的取消令牌传递，不在测试夹具建立第二套授权或分页逻辑。</summary>
        internal Task<ServiceResult<PagedResponseBase<InventorySupplierLookupDto>>> CallAsync(bool useInbound,PagedRequestBase request,CancellationToken token)
            =>useInbound?new InboundSourcesController(inbound).GetSupplierOptionsAsync(request,token):new InventoryController(inventory).GetSupplierOptionsAsync(request,token);
    }
}

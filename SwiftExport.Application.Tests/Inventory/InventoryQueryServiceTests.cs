using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Inventory.Query;
using SwiftExport.Application.Services.Inventory.Warehouses;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Inventory.Query;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.WebApi.Controllers.Inventory.Query;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>离线执行库存与仓库查询服务，验证统一输入上限和库存筛选候选的独立权限。</summary>
[TestClass]
public sealed class InventoryQueryServiceTests
{
    /// <summary>库存关键词接受二百字符，仓库管理接受一百字符；超限不得截断或调用仓储。</summary>
    [TestMethod]
    [DataRow(0,200,true)] [DataRow(0,201,false)]
    [DataRow(1,200,true)] [DataRow(1,201,false)]
    [DataRow(2,200,true)] [DataRow(2,201,false)]
    [DataRow(3,100,true)] [DataRow(3,101,false)]
    public async Task KeywordLimitsRejectBeforeRepository(int mode,int length,bool accepted)
    {
        var fixture=new Fixture(permission:mode==3?"Inventory.Warehouse.View":"Inventory.Balance.View");
        var request=Request(mode);request.Keyword=new string('仓',length);
        var result=await fixture.QueryAsync(mode,request);
        Assert.AreEqual(accepted,result.Success,result.Message);
        Assert.AreEqual(accepted?1:0,fixture.Calls);
        Assert.AreEqual(length,request.Keyword.Length);
    }

    /// <summary>四个查询入口接受二百条分页并拒绝无效页码、空页及超限页，合法条件原样传递。</summary>
    [TestMethod]
    [DataRow(0,1,200,true)] [DataRow(0,1,201,false)] [DataRow(0,0,20,false)] [DataRow(0,1,0,false)]
    [DataRow(1,1,200,true)] [DataRow(1,1,201,false)] [DataRow(1,0,20,false)] [DataRow(1,1,0,false)]
    [DataRow(2,1,200,true)] [DataRow(2,1,201,false)] [DataRow(2,0,20,false)] [DataRow(2,1,0,false)]
    [DataRow(3,1,200,true)] [DataRow(3,1,201,false)] [DataRow(3,0,20,false)] [DataRow(3,1,0,false)]
    public async Task PagingLimitsRejectBeforeRepository(int mode,int pageIndex,int pageSize,bool accepted)
    {
        var fixture=new Fixture(permission:mode==3?"Inventory.Warehouse.View":"Inventory.Balance.View");var request=Request(mode);request.PageIndex=pageIndex;request.PageSize=pageSize;
        var result=await fixture.QueryAsync(mode,request);
        Assert.AreEqual(accepted,result.Success,result.Message);
        Assert.AreEqual(accepted?1:0,fixture.Calls);
        if(accepted){Assert.AreEqual(pageIndex,fixture.LastRequest!.PageIndex);Assert.AreEqual(pageSize,fixture.LastRequest.PageSize);}
    }

    /// <summary>流水保留来源单号一百字符上限与日期先后校验，同日及单侧日期不误拒绝。</summary>
    [TestMethod]
    [DataRow(100,0,true)] [DataRow(101,0,false)] [DataRow(100,-1,false)] [DataRow(100,1,true)] [DataRow(100,2,true)]
    public async Task LedgerSourceAndDateLimitsRemainEffective(int sourceLength,int dateMode,bool accepted)
    {
        var fixture=new Fixture();var date=new DateTime(2026,10,21);
        var request=new InventoryLedgerSearchRequest{SourceDocumentNo=new string('单',sourceLength),OccurredDateFrom=date,
            OccurredDateTo=dateMode==2?null:date.AddDays(dateMode)};
        var result=await fixture.Inventory.SearchLedgerAsync(request);
        Assert.AreEqual(accepted,result.Success,result.Message);
        Assert.AreEqual(accepted?1:0,fixture.Calls);
        Assert.AreEqual(sourceLength,request.SourceDocumentNo.Length);
        if(accepted) Assert.AreSame(request,fixture.LastRequest);
    }

    /// <summary>库存权限允许完整分页候选；仓库管理权限不能替代库存权限，未认证或无权限不读仓储。</summary>
    [TestMethod]
    [DataRow(false,"Inventory.Balance.View",false)]
    [DataRow(true,"",false)]
    [DataRow(true,"Inventory.Warehouse.View",false)]
    [DataRow(true,"Inventory.Balance.View",true)]
    public async Task InventoryOptionsRequireExactPermission(bool authenticated,string permission,bool accepted)
    {
        var fixture=new Fixture(authenticated,permission);
        using var cancellation=new CancellationTokenSource();
        var request=new PagedRequestBase{Keyword="  仓库  ",PageIndex=3,PageSize=200};
        var result=await fixture.Inventory.GetWarehouseOptionsAsync(request,cancellation.Token);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,fixture.Calls);
        Assert.AreEqual("  仓库  ",request.Keyword);
        if(!accepted) Assert.AreEqual(ServiceErrorCodes.Forbidden,result.Error.Code);
        else
        {
            Assert.AreEqual("GetPickerPagedAsync",fixture.LastMethod);
            Assert.AreEqual("仓库",fixture.LastRequest!.Keyword);
            Assert.AreEqual(3,fixture.LastRequest.PageIndex);Assert.AreEqual(200,fixture.LastRequest.PageSize);
            Assert.AreEqual(cancellation.Token,fixture.LastToken);Assert.AreEqual(cancellation.Token,fixture.PermissionToken);
            Assert.AreSame(fixture.PickerPage,result.Data);Assert.AreEqual(601,result.Data.TotalCount);
        }
        if(authenticated) Assert.AreEqual("Inventory.Balance.View",fixture.CheckedKeys.Single());
        else Assert.IsEmpty(fixture.CheckedKeys);
    }

    /// <summary>余额查看权限不得读取仓库管理列表或旧管理选择器，不为筛选用途放宽整个仓库服务。</summary>
    [TestMethod]
    public async Task BalancePermissionCannotReadWarehouseManagement()
    {
        var fixture=new Fixture();
        Assert.IsFalse((await fixture.Warehouses.SearchWarehouseSitesAsync(new())).Success);
        Assert.IsFalse((await fixture.Warehouses.GetWarehousePickerPagedAsync(new())).Success);
        Assert.AreEqual(0,fixture.Calls);
        Assert.IsTrue(fixture.CheckedKeys.All(key=>key=="Inventory.Warehouse.View"));
    }

    /// <summary>候选取消沿真实 Controller 和 Service 传到仓储并向上抛出，不转换成成功空候选。</summary>
    [TestMethod]
    public async Task OptionsCancellationPropagatesThroughController()
    {
        var fixture=new Fixture();using var cancellation=new CancellationTokenSource();
        fixture.CancelPicker=true;cancellation.Cancel();
        var controller=new InventoryController(fixture.Inventory);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>controller.GetWarehouseOptionsAsync(new(),cancellation.Token));
        Assert.AreEqual(1,fixture.Calls);Assert.AreEqual(cancellation.Token,fixture.LastToken);
    }

    /// <summary>库存候选的 Refit 与 HTTP 查询绑定保持一致，继续返回原候选 DTO 和分页包装。</summary>
    [TestMethod]
    public void OptionsContractUsesQueryAndExistingPickerDto()
    {
        var api=typeof(IInventoryApi).GetMethod(nameof(IInventoryApi.GetWarehouseOptionsAsync))!;
        var action=typeof(InventoryController).GetMethod(nameof(InventoryController.GetWarehouseOptionsAsync))!;
        Assert.IsTrue(api.GetParameters()[0].IsDefined(typeof(QueryAttribute),false));
        Assert.IsTrue(action.GetParameters()[0].IsDefined(typeof(FromQueryAttribute),false));
        Assert.AreEqual(typeof(PagedRequestBase),action.GetParameters()[0].ParameterType);
        Assert.AreEqual(typeof(Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>),api.ReturnType);
        Assert.AreEqual(api.ReturnType,action.ReturnType);
    }

    /// <summary>为每个真实用例提供对应请求，候选沿用平台分页契约。</summary>
    private static PagedRequestBase Request(int mode)=>mode switch
    {0=>new InventoryBalanceSearchRequest(),1=>new InventoryLedgerSearchRequest(),2=>new PagedRequestBase(),_=>new WarehouseSiteSearchRequest()};

    /// <summary>仅替换用户、权限与仓储端口；任何数据库会话访问都立即失败。</summary>
    private sealed class Fixture
    {
        internal InventoryQueryService Inventory {get;}
        internal WarehouseQueryService Warehouses {get;}
        internal int Calls {get;private set;}
        internal string? LastMethod {get;private set;}
        internal PagedRequestBase? LastRequest {get;private set;}
        internal CancellationToken LastToken {get;private set;}
        internal CancellationToken PermissionToken {get;private set;}
        internal List<string> CheckedKeys {get;}=[];
        internal bool CancelPicker {get;set;}
        internal PagedResponseBase<WarehouseSitePickerDto> PickerPage {get;}=new(){PageIndex=3,PageSize=200,TotalCount=601,Items=[new(){Uid=Guid.NewGuid(),Name="候选仓"}]};

        /// <summary>按明确权限键答复，区分库存候选和仓库管理权限，不伪造数据库行为。</summary>
        internal Fixture(bool authenticated=true,string permission="Inventory.Balance.View")
        {
            var context=new ServiceBaseContext(PurchaseDraftSourceOrchestrationTests.Proxy<IDbSession>.New((method,_)=>throw new AssertFailedException(method.Name)),NullLoggerFactory.Instance);
            var user=PurchaseDraftSourceOrchestrationTests.Proxy<ICurrentUser>.New((method,_)=>method.Name switch
            {"get_IsAuthenticated"=>authenticated,"get_UserId"=>(Guid?)Guid.Parse("d6bfbb81-2874-4113-9a57-501f43caf826"),_=>throw new AssertFailedException(method.Name)});
            var permissions=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>
            {
                Assert.AreEqual("HasPermissionAsync",method.Name);CheckedKeys.Add((string)args[1]!);PermissionToken=(CancellationToken)args[2]!;
                return Task.FromResult(permission==(string)args[1]!);
            });
            var inventory=PurchaseDraftSourceOrchestrationTests.Proxy<IInventoryQueryRepository>.New((method,args)=>Record(method.Name,args));
            var warehouse=PurchaseDraftSourceOrchestrationTests.Proxy<IWarehouseQueryRepository>.New((method,args)=>Record(method.Name,args));
            Inventory=new(context,inventory,user,permissions,warehouse);Warehouses=new(context,warehouse,user,permissions);
        }

        /// <summary>按真实方法返回不同分页类型并观察请求，取消仅在候选仓储边界模拟。</summary>
        private object Record(string method,object?[] args)
        {
            Calls++;LastMethod=method;LastRequest=(PagedRequestBase)args[0]!;LastToken=(CancellationToken)args[1]!;
            return method switch
            {
                "SearchBalancesAsync"=>Task.FromResult(new PagedResponseBase<InventoryBalanceDto>()),
                "SearchLedgerAsync"=>Task.FromResult(new PagedResponseBase<InventoryLedgerItemDto>()),
                "SearchAsync"=>Task.FromResult(new PagedResponseBase<WarehouseSiteDto>()),
                "GetPickerPagedAsync"=>CancelPicker?Task.FromCanceled<PagedResponseBase<WarehouseSitePickerDto>>(LastToken):Task.FromResult(PickerPage),
                _=>throw new AssertFailedException(method)
            };
        }

        /// <summary>保留各实际服务入口及其响应，不建立第二套校验逻辑。</summary>
        internal async Task<IServiceResult> QueryAsync(int mode,PagedRequestBase request)=>mode switch
        {
            0=>await Inventory.SearchBalancesAsync((InventoryBalanceSearchRequest)request),
            1=>await Inventory.SearchLedgerAsync((InventoryLedgerSearchRequest)request),
            2=>await Inventory.GetWarehouseOptionsAsync(request),
            _=>await Warehouses.SearchWarehouseSitesAsync((WarehouseSiteSearchRequest)request)
        };
    }
}

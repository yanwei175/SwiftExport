using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Inventory.Inbounds;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.Apis.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Data;
using SwiftExport.WebApi.Controllers.Inventory.Inbounds;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>通过真实入库读取支持验证查询上限、独立仓库候选权限及取消传递，无数据库依赖。</summary>
[TestClass]
public sealed class InboundQueryOptionsTests
{
    /// <summary>非 null 集合覆盖旧单值且逐项校验；空集合不限，未传集合时继续校验旧单值。</summary>
    [TestMethod]
    [DataRow(true,0,true)] [DataRow(false,0,true)]
    [DataRow(true,1,true)] [DataRow(false,1,true)]
    [DataRow(true,2,false)] [DataRow(false,2,false)]
    [DataRow(true,3,false)] [DataRow(false,3,false)]
    public async Task SearchValidatesEffectiveEnumCollections(bool documentType,int mode,bool accepted)
    {
        var f=new Fixture();var request=new InboundManagerSearchRequest();
        if(documentType)
        {
            request.DocumentType=(InboundDocumentTypeEnum)999;
            request.DocumentTypes=mode switch
            {
                0=>[InboundDocumentTypeEnum.PurchaseReceipt,InboundDocumentTypeEnum.ProductionReceipt],
                1=>[],2=>[InboundDocumentTypeEnum.PurchaseReceipt,(InboundDocumentTypeEnum)999],_=>null!
            };
        }
        else
        {
            request.PostingStatus=(InboundPostingStatusEnum)999;
            request.PostingStatuses=mode switch
            {
                0=>[InboundPostingStatusEnum.Unposted,InboundPostingStatusEnum.Reversed],
                1=>[],2=>[InboundPostingStatusEnum.Unposted,(InboundPostingStatusEnum)999],_=>null!
            };
        }
        var result=await f.Query.SearchAsync(request);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Calls);
        if(accepted)Assert.AreSame(request,f.LastRequest);
        if(mode==2)
        {
            Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);
            Assert.AreEqual(documentType?nameof(request.DocumentTypes):nameof(request.PostingStatuses),result.Error.Field);
        }
    }

    /// <summary>管理查询、来源搜索及仓库候选统一接受二百字符/二百条，超限不访问仓储且不截断输入。</summary>
    [TestMethod]
    [DataRow(0,200,200,true)] [DataRow(0,201,20,false)] [DataRow(0,1,201,false)]
    [DataRow(1,200,200,true)] [DataRow(1,201,20,false)] [DataRow(1,1,201,false)]
    [DataRow(2,200,200,true)] [DataRow(2,201,20,false)] [DataRow(2,1,201,false)]
    public async Task QueryLimitsAreEnforcedBeforeRepository(int mode,int length,int pageSize,bool accepted)
    {
        var f=new Fixture();PagedRequestBase request=mode switch
        {
            0=>new InboundManagerSearchRequest(),
            1=>new InboundSourceSearchRequest{DocumentType=InboundDocumentTypeEnum.PurchaseReceipt,PurchaseCategory=InboundPurchaseCategoryEnum.Normal},
            _=>new PagedRequestBase()
        };
        request.Keyword=new string('仓',length);request.PageSize=pageSize;
        IServiceResult result=mode switch
        {
            0=>await f.Query.SearchAsync((InboundManagerSearchRequest)request),
            1=>await f.Sources.SearchSourcesAsync((InboundSourceSearchRequest)request),
            _=>await f.Query.GetWarehouseOptionsAsync(request)
        };
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Calls);
        Assert.AreEqual(length,request.Keyword.Length);
    }

    /// <summary>只有入库查看权限可读取完整候选分页；未认证或其他管理查看权限均在仓储前拒绝。</summary>
    [TestMethod]
    [DataRow(false,"Inventory.Inbound.View",false)]
    [DataRow(true,"",false)] [DataRow(true,"Inventory.Warehouse.View",false)]
    [DataRow(true,"Inventory.Balance.View",false)] [DataRow(true,"Inventory.Inbound.View",true)]
    public async Task WarehouseOptionsRequireInboundPermission(bool authenticated,string permission,bool accepted)
    {
        var f=new Fixture(authenticated,permission);using var cancellation=new CancellationTokenSource();
        var request=new PagedRequestBase{Keyword="  仓库  ",PageIndex=3,PageSize=200};
        var result=await new InboundQueryController(f.Query).GetWarehouseOptionsAsync(request,cancellation.Token);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Calls);
        Assert.AreEqual("  仓库  ",request.Keyword);
        if(accepted)
        {
            Assert.AreSame(f.Page,result.Data);Assert.AreEqual(501,result.Data.TotalCount);
            Assert.AreEqual(3,f.LastRequest!.PageIndex);Assert.AreEqual(200,f.LastRequest.PageSize);
            Assert.AreEqual("仓库",f.LastRequest.Keyword);Assert.AreEqual(cancellation.Token,f.Token);
        }
        else Assert.AreEqual(authenticated?ServiceErrorCodes.Forbidden:ServiceErrorCodes.Unauthenticated,result.Error.Code);
        if(authenticated)Assert.AreEqual("Inventory.Inbound.View",f.Keys.Single());else Assert.IsEmpty(f.Keys);
    }

    /// <summary>候选仓储收到原取消令牌；执行期间取消原样抛出，不转成成功空结果。</summary>
    [TestMethod]
    public async Task OptionsCancellationFlowsThroughControllerAndService()
    {
        var f=new Fixture();using var cancellation=new CancellationTokenSource();f.CancelPicker=cancellation.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(()=>new InboundQueryController(f.Query).GetWarehouseOptionsAsync(new(),cancellation.Token));
        Assert.AreEqual(1,f.Calls);Assert.AreEqual(cancellation.Token,f.Token);
    }

    /// <summary>新端点采用 GET 查询绑定和原仓库候选 DTO，不增加与管理操作混合的传输字段。</summary>
    [TestMethod]
    public void OptionsRouteAndRefitContractMatch()
    {
        var api=typeof(IInboundManagerApi).GetMethod(nameof(IInboundManagerApi.GetWarehouseOptionsAsync))!;
        var action=typeof(InboundQueryController).GetMethod(nameof(InboundQueryController.GetWarehouseOptionsAsync))!;
        Assert.AreEqual("/warehouses/picker",api.GetCustomAttributes(typeof(GetAttribute),false).Cast<GetAttribute>().Single().Path);
        Assert.AreEqual("warehouses/picker",action.GetCustomAttributes(typeof(HttpGetAttribute),false).Cast<HttpGetAttribute>().Single().Template);
        Assert.IsTrue(api.GetParameters()[0].IsDefined(typeof(QueryAttribute),false));
        Assert.IsTrue(action.GetParameters()[0].IsDefined(typeof(FromQueryAttribute),false));
        Assert.AreEqual(typeof(Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>),api.ReturnType);
        Assert.AreEqual(api.ReturnType,action.ReturnType);
    }

    /// <summary>只替换真实读取支持的边界端口，记录请求与令牌，不初始化会话或数据库。</summary>
    private sealed class Fixture
    {
        internal InboundQueryService Query {get;}
        internal InboundSourceQueryService Sources {get;}
        internal int Calls {get;private set;}
        internal List<string> Keys {get;}=[];
        internal PagedRequestBase? LastRequest {get;private set;}
        internal CancellationToken Token {get;private set;}
        internal Action? CancelPicker {get;set;}
        internal PagedResponseBase<WarehouseSitePickerDto> Page {get;}=new(){PageIndex=3,PageSize=200,TotalCount=501};

        /// <summary>明确区分身份和入库查看权限，所有仓储响应均由可观察替身提供。</summary>
        internal Fixture(bool authenticated=true,string permission="Inventory.Inbound.View")
        {
            var user=PurchaseDraftSourceOrchestrationTests.Proxy<ICurrentUser>.New((method,_)=>method.Name switch
            {"get_IsAuthenticated"=>authenticated,"get_UserId"=>(Guid?)Guid.NewGuid(),_=>throw new AssertFailedException(method.Name)});
            var permissions=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>
            {Assert.AreEqual("HasPermissionAsync",method.Name);Keys.Add((string)args[1]!);return Task.FromResult((string)args[1]! == permission);});
            var support=new InboundServiceSupport(user,permissions,NullLogger<InboundServiceSupport>.Instance,PostgreSqlExceptionClassifier.Instance);
            var queries=PurchaseDraftSourceOrchestrationTests.Proxy<IInboundQueryRepository>.New((method,args)=>
            {Assert.AreEqual("SearchAsync",method.Name);Calls++;LastRequest=(PagedRequestBase)args[0]!;return Task.FromResult(new PagedResponseBase<InboundManagerListItemDto>());});
            var sources=PurchaseDraftSourceOrchestrationTests.Proxy<IInboundSourceRepository>.New((method,_)=>
            {Assert.AreEqual("SearchSourcesAsync",method.Name);Calls++;return Task.FromResult(new PagedResponseBase<InboundSourceCandidateDto>());});
            var warehouses=PurchaseDraftSourceOrchestrationTests.Proxy<IWarehouseQueryRepository>.New((method,args)=>
            {
                Assert.AreEqual("GetPickerPagedAsync",method.Name);Calls++;LastRequest=(PagedRequestBase)args[0]!;Token=(CancellationToken)args[1]!;
                CancelPicker?.Invoke();return Token.IsCancellationRequested?Task.FromCanceled<PagedResponseBase<WarehouseSitePickerDto>>(Token):Task.FromResult(Page);
            });
            Query=new(queries,support,warehouses);Sources=new(sources,support,warehouses);
        }
    }
}

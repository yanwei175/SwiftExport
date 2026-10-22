using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Inventory.Outbounds;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.Apis.Inventory;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.WareHouseSite.Picker;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Inventory.Outbounds;
using SwiftExport.Core.Interfaces.Inventory.Warehouses;
using SwiftExport.Infrastructure.Data;
using SwiftExport.WebApi.Controllers.Inventory.Outbounds;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>执行真实出库查询服务和 Refit 序列化，验证多选兼容、字段错误及请求上限，不连接数据库。</summary>
[TestClass]
public sealed class OutboundQueryValidationTests
{
    /// <summary>出库查看权限独立读取完整仓库分页；匿名和无查看权限不能进入仓储。</summary>
    [TestMethod]
    [DataRow(false,true,false)] [DataRow(true,false,false)] [DataRow(true,true,true)]
    public async Task WarehouseOptionsUseOutboundViewAndFullPaging(bool authenticated,bool allowed,bool accepted)
    {
        var f=new Fixture(authenticated,allowed);using var cancellation=new CancellationTokenSource();
        var request=new PagedRequestBase{Keyword="  仓库  ",PageIndex=2,PageSize=200};
        var result=await new OutboundQueryController(f.Query).GetWarehouseOptionsAsync(request,cancellation.Token);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Calls);
        Assert.AreEqual("  仓库  ",request.Keyword);
        if(accepted)
        {
            Assert.AreSame(f.WarehousePage,result.Data);Assert.AreEqual(501,result.Data.TotalCount);
            var forwarded=(PagedRequestBase)f.LastRequest!;
            Assert.AreEqual("仓库",forwarded.Keyword);Assert.AreEqual(2,forwarded.PageIndex);Assert.AreEqual(200,forwarded.PageSize);
            Assert.AreEqual(cancellation.Token,f.Token);
        }
        else Assert.AreEqual(authenticated?ServiceErrorCodes.Forbidden:ServiceErrorCodes.Unauthenticated,result.Error.Code);
    }

    /// <summary>仓库入口校验上限、HTTP 正文绑定和执行中取消，不把失败或取消转换为空候选。</summary>
    [TestMethod]
    public async Task WarehouseOptionsValidateAndPreserveCancellation()
    {
        var f=new Fixture();var invalid=await f.Query.GetWarehouseOptionsAsync(new(){Keyword=new string('仓',201)});
        Assert.AreEqual(ServiceErrorCodes.Validation,invalid.Error.Code);Assert.AreEqual("Keyword",invalid.Error.Field);Assert.AreEqual(0,f.Calls);
        using var cancellation=new CancellationTokenSource();f.CancelWarehouse=cancellation.Cancel;
        await Assert.ThrowsAsync<OperationCanceledException>(()=>new OutboundQueryController(f.Query).GetWarehouseOptionsAsync(new(),cancellation.Token));
        Assert.AreEqual(cancellation.Token,f.Token);Assert.AreEqual(1,f.Calls);
        var api=typeof(IOutboundManagerApi).GetMethod(nameof(IOutboundManagerApi.GetWarehouseOptionsAsync))!;
        var action=typeof(OutboundQueryController).GetMethod(nameof(OutboundQueryController.GetWarehouseOptionsAsync))!;
        Assert.IsTrue(api.GetParameters()[0].IsDefined(typeof(BodyAttribute),false));
        Assert.IsTrue(action.GetParameters()[0].IsDefined(typeof(FromBodyAttribute),false));
        Assert.AreEqual(typeof(Task<ServiceResult<PagedResponseBase<WarehouseSitePickerDto>>>),action.ReturnType);
    }

    /// <summary>非 null 集合覆盖旧单值并逐项校验；空集合不限，未传集合才验证旧单值。</summary>
    [TestMethod]
    [DataRow(true,0,true)] [DataRow(false,0,true)]
    [DataRow(true,1,true)] [DataRow(false,1,true)]
    [DataRow(true,2,false)] [DataRow(false,2,false)]
    [DataRow(true,3,false)] [DataRow(false,3,false)]
    public async Task EnumCollectionsUseEffectiveConditions(bool documentType,int mode,bool accepted)
    {
        var f=new Fixture();var request=new OutboundManagerSearchRequest();
        if(documentType)
        {
            request.DocumentType=(OutboundDocumentTypeEnum)999;
            request.DocumentTypes=mode switch
            {0=>[OutboundDocumentTypeEnum.StockIssue,OutboundDocumentTypeEnum.FinalCustomerShipment],1=>[],2=>[OutboundDocumentTypeEnum.StockIssue,(OutboundDocumentTypeEnum)999],_=>null!};
        }
        else
        {
            request.PostingStatus=(OutboundPostingStatusEnum)999;
            request.PostingStatuses=mode switch
            {0=>[OutboundPostingStatusEnum.Unposted,OutboundPostingStatusEnum.Reversed],1=>[],2=>[OutboundPostingStatusEnum.Unposted,(OutboundPostingStatusEnum)999],_=>null!};
        }
        var result=await f.Query.SearchAsync(request);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Calls);
        if(accepted)Assert.AreSame(request,f.LastRequest);
        else
        {
            Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);
            Assert.AreEqual(documentType?(mode==2?"DocumentTypes":"DocumentType"):(mode==2?"PostingStatuses":"PostingStatus"),result.Error.Field);
        }
    }

    /// <summary>列表与选品的结构错误均返回稳定 Validation 和字段，拒绝时不进入仓储。</summary>
    [TestMethod]
    [DataRow(false,"Request")] [DataRow(true,"Request")]
    [DataRow(false,"PageIndex")] [DataRow(true,"PageIndex")]
    [DataRow(false,"PageSize")] [DataRow(true,"PageSize")]
    [DataRow(false,"Keyword")] [DataRow(true,"Keyword")]
    [DataRow(true,"WarehouseSiteId")] [DataRow(true,"ExcludedProductIds")]
    public async Task InvalidQueryHasStableCodeAndField(bool items,string field)
    {
        var f=new Fixture();PagedRequestBase? request=items?new OutboundItemSearchRequest{WarehouseSiteId=Guid.NewGuid()}:new OutboundManagerSearchRequest();
        switch(field)
        {
            case "Request":request=null;break;
            case "PageIndex":request.PageIndex=0;break;
            case "PageSize":request.PageSize=201;break;
            case "Keyword":request.Keyword=new string('出',201);break;
            case "WarehouseSiteId":((OutboundItemSearchRequest)request).WarehouseSiteId=Guid.Empty;break;
            case "ExcludedProductIds":((OutboundItemSearchRequest)request).ExcludedProductIds=null!;break;
        }
        IServiceResult result=items?await f.Query.SearchItemsAsync((OutboundItemSearchRequest)request!):await f.Query.SearchAsync((OutboundManagerSearchRequest)request!);
        Assert.IsFalse(result.Success);Assert.AreEqual(ServiceErrorCodes.Validation,result.Error.Code);Assert.AreEqual(field,result.Error.Field);Assert.AreEqual(0,f.Calls);
    }

    /// <summary>二百字符、二百条分页与五百排除产品可原样读取；五百零一项不访问仓储。</summary>
    [TestMethod]
    [DataRow(500,true)] [DataRow(501,false)]
    public async Task PickerLimitsDoNotTruncateInput(int excludedCount,bool accepted)
    {
        var f=new Fixture();var request=new OutboundItemSearchRequest{WarehouseSiteId=Guid.NewGuid(),Keyword=new string('仓',200),PageSize=200,
            ExcludedProductIds=Enumerable.Range(0,excludedCount).Select(_=>Guid.NewGuid()).ToList()};
        var result=await f.Query.SearchItemsAsync(request);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Calls);
        Assert.AreEqual(excludedCount,request.ExcludedProductIds.Count);Assert.AreEqual(200,request.Keyword.Length);
        if(accepted)Assert.AreSame(request,f.LastRequest);else Assert.AreEqual("ExcludedProductIds",result.Error.Field);
        Assert.IsTrue((await new Fixture().Query.SearchAsync(new(){Keyword=new string('字',200),PageSize=200})).Success);
    }

    /// <summary>空详情身份是可定位的请求错误；身份和读取权限失效时优先拒绝，不访问仓储。</summary>
    [TestMethod]
    public async Task IdentityAndPermissionRejectBeforeRepository()
    {
        var f=new Fixture();var empty=await f.Query.GetAsync(Guid.Empty);
        Assert.AreEqual(ServiceErrorCodes.Validation,empty.Error.Code);Assert.AreEqual("Uid",empty.Error.Field);Assert.AreEqual(0,f.Calls);
        foreach(var authenticated in new[]{false,true})
        {
            var denied=new Fixture(authenticated,allowed:false);var result=await denied.Query.SearchAsync(null!);
            Assert.AreEqual(authenticated?ServiceErrorCodes.Forbidden:ServiceErrorCodes.Unauthenticated,result.Error.Code);Assert.AreEqual(0,denied.Calls);
        }
    }

    /// <summary>实际 Refit 请求经服务端 Newtonsoft 还原后保留 null、空集合与旧单值，避免只验证内存对象。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2)]
    public async Task RefitPayloadPreservesNullEmptyAndLegacy(int mode)
    {
        var request=new OutboundManagerSearchRequest{DocumentType=OutboundDocumentTypeEnum.StockIssue,PostingStatus=OutboundPostingStatusEnum.Posted};
        if(mode==1){request.DocumentTypes=[];request.PostingStatuses=[];}
        if(mode==2){request.DocumentTypes=[OutboundDocumentTypeEnum.FinalCustomerShipment,OutboundDocumentTypeEnum.StockIssue];request.PostingStatuses=[OutboundPostingStatusEnum.Unposted,OutboundPostingStatusEnum.Reversed];}
        using var handler=new CaptureHandler();using var client=new HttpClient(handler){BaseAddress=new Uri("http://outbound.test/api/inventory/outbounds")};
        var api=RestService.For<IOutboundManagerApi>(client);Assert.IsTrue((await api.SearchAsync(request)).Success);
        Assert.IsNotNull(handler.Body);var restored=JsonConvert.DeserializeObject<OutboundManagerSearchRequest>(handler.Body)!;
        Assert.AreEqual(request.DocumentType,restored.DocumentType);Assert.AreEqual(request.PostingStatus,restored.PostingStatus);
        if(mode==0){Assert.IsNull(restored.DocumentTypes);Assert.IsNull(restored.PostingStatuses);}
        else
        {
            using var json=JsonDocument.Parse(handler.Body);
            Assert.AreEqual(JsonValueKind.Array,json.RootElement.GetProperty("documentTypes").ValueKind);
            Assert.AreEqual(JsonValueKind.Array,json.RootElement.GetProperty("postingStatuses").ValueKind);
            CollectionAssert.AreEqual(request.DocumentTypes,restored.DocumentTypes);CollectionAssert.AreEqual(request.PostingStatuses,restored.PostingStatuses);
        }
        var f=new Fixture();Assert.IsTrue((await f.Query.SearchAsync(restored)).Success);Assert.AreSame(restored,f.LastRequest);
    }

    /// <summary>只捕获本地序列化请求并回复合法分页，不向外部网络发送 HTTP。</summary>
    private sealed class CaptureHandler:HttpMessageHandler
    {
        internal string? Body {get;private set;}
        /// <summary>读取真实 Refit 请求正文，返回当前契约的确定结果。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Body=await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK){Content=new StringContent("{\"success\":true,\"data\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":20}}",Encoding.UTF8,"application/json")};
        }
    }

    /// <summary>只替换真实读取服务的边界端口，不建立数据库会话。</summary>
    private sealed class Fixture
    {
        internal OutboundQueryService Query {get;}
        internal int Calls {get;private set;}
        internal object? LastRequest {get;private set;}
        internal CancellationToken Token {get;private set;}
        internal Action? CancelWarehouse {get;set;}
        internal PagedResponseBase<WarehouseSitePickerDto> WarehousePage {get;}=new(){PageIndex=2,PageSize=200,TotalCount=501};
        /// <summary>提供当前身份和精确出库查看权限，记录实际读取请求。</summary>
        internal Fixture(bool authenticated=true,bool allowed=true)
        {
            var session=PurchaseDraftSourceOrchestrationTests.Proxy<IDbSession>.New((method,_)=>method.Name=="get_Errors"?PostgreSqlExceptionClassifier.Instance:throw new AssertFailedException(method.Name));
            var user=PurchaseDraftSourceOrchestrationTests.Proxy<ICurrentUser>.New((method,_)=>method.Name switch
            {"get_IsAuthenticated"=>authenticated,"get_UserId"=>(Guid?)Guid.Parse("d6bfbb81-2874-4113-9a57-501f43caf826"),_=>throw new AssertFailedException(method.Name)});
            var permissions=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>
            {Assert.AreEqual("HasPermissionAsync",method.Name);Assert.AreEqual("Inventory.Outbound.View",args[1]);return Task.FromResult(allowed);});
            var repository=PurchaseDraftSourceOrchestrationTests.Proxy<IOutboundQueryRepository>.New((method,args)=>
            {
                Calls++;LastRequest=args[0];return method.Name switch
                {"SearchAsync"=>Task.FromResult(new PagedResponseBase<OutboundManagerListItemDto>()),"SearchItemsAsync"=>Task.FromResult(new PagedResponseBase<OutboundItemCandidateDto>()),_=>throw new AssertFailedException(method.Name)};
            });
            var warehouses=PurchaseDraftSourceOrchestrationTests.Proxy<IWarehouseQueryRepository>.New((method,args)=>
            {
                Assert.AreEqual("GetPickerPagedAsync",method.Name);Calls++;LastRequest=args[0];Token=(CancellationToken)args[1]!;
                CancelWarehouse?.Invoke();return Token.IsCancellationRequested?Task.FromCanceled<PagedResponseBase<WarehouseSitePickerDto>>(Token):Task.FromResult(WarehousePage);
            });
            Query=new(repository,new OutboundServiceSupport(new ServiceBaseContext(session,NullLoggerFactory.Instance),user,permissions),warehouses);
        }
    }
}

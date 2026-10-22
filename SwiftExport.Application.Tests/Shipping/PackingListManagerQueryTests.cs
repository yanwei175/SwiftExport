using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Refit;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Shipping.PackingLists;
using SwiftExport.Application.Tests.Procurement;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Data;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>只执行 Manager 查询，验证集合优先级、输入拒绝及旧 Refit 调用兼容，不涉及来源或详情。</summary>
[TestClass]
public sealed class PackingListManagerQueryTests
{
    /// <summary>非 null 集合覆盖旧条件并完整校验；空集合不限，null 才回退旧单值。</summary>
    [TestMethod]
    [DataRow(true,0,true)] [DataRow(false,0,true)]
    [DataRow(true,1,true)] [DataRow(false,1,true)]
    [DataRow(true,2,false)] [DataRow(false,2,false)]
    [DataRow(true,3,false)] [DataRow(false,3,false)]
    public async Task EffectiveCollectionsAreValidated(bool customers,int mode,bool accepted)
    {
        var f=new Fixture();var request=new PackingListManagerSearchRequest();
        if(customers)
        {
            request.CustomerId=Guid.Empty;
            request.CustomerIds=mode switch{0=>[Guid.NewGuid(),Guid.NewGuid()],1=>[],2=>[Guid.NewGuid(),Guid.Empty],_=>null!};
        }
        else
        {
            request.PostingStatus=(PackingListPostingStatusEnum)999;
            request.PostingStatuses=mode switch
            {0=>[PackingListPostingStatusEnum.Unposted,PackingListPostingStatusEnum.Reversed],1=>[],2=>[PackingListPostingStatusEnum.Unposted,(PackingListPostingStatusEnum)999],_=>null!};
        }
        var result=await f.Service.SearchAsync(request);
        Assert.AreEqual(accepted,result.Success,result.Message);Assert.AreEqual(accepted?1:0,f.Searches);Assert.AreEqual(1,f.SchemaReads);
        if(accepted)Assert.AreSame(request,f.Request);
        else
        {
            Assert.AreEqual(PackingListErrorCodes.InvalidDraft,result.Error.Code);
            Assert.AreEqual(customers?(mode==2?"CustomerIds":"CustomerId"):(mode==2?"PostingStatuses":"PostingStatus"),result.Error.Field);
        }
    }

    /// <summary>原查看权限与结构门禁仍优先，日期及分页旧口径不随多选改动。</summary>
    [TestMethod]
    public async Task ManagerKeepsAuthorizationSchemaDatesAndPaging()
    {
        var denied=new Fixture(allowed:false);Assert.IsFalse((await denied.Service.SearchAsync(new())).Success);
        Assert.AreEqual(0,denied.SchemaReads);Assert.AreEqual(0,denied.Searches);
        var f=new Fixture();var invalid=await f.Service.SearchAsync(new(){InvoiceDateFrom=new(2026,10,22),InvoiceDateTo=new(2026,10,21)});
        Assert.AreEqual("InvoiceDateFrom",invalid.Error.Field);Assert.AreEqual(0,f.Searches);
        var request=new PackingListManagerSearchRequest{PageIndex=0,PageSize=500,CustomerIds=[],PostingStatuses=[]};
        Assert.IsTrue((await f.Service.SearchAsync(request)).Success);Assert.AreEqual(1,request.PageIndex);Assert.AreEqual(200,request.PageSize);
        f.Ready=false;var unavailable=await f.Service.SearchAsync(new());Assert.AreEqual(PackingListErrorCodes.DeliveryUnavailable,unavailable.Error.Code);Assert.AreEqual(1,f.Searches);
    }

    /// <summary>真实 Refit 正文经 Newtonsoft 还原后区分 null、空集合和多选，同时保留旧单值字段。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2)]
    public async Task RefitKeepsCollectionAndLegacyWireSemantics(int mode)
    {
        var request=new PackingListManagerSearchRequest{CustomerId=Guid.NewGuid(),PostingStatus=PackingListPostingStatusEnum.Posted};
        if(mode==1){request.CustomerIds=[];request.PostingStatuses=[];}
        if(mode==2){request.CustomerIds=[Guid.NewGuid(),Guid.NewGuid()];request.PostingStatuses=[PackingListPostingStatusEnum.Unposted,PackingListPostingStatusEnum.Reversed];}
        using var handler=new CaptureHandler();using var client=new HttpClient(handler){BaseAddress=new Uri("http://packing-list.test/api/packing-lists")};
        Assert.IsTrue((await RestService.For<IPackingListApi>(client).SearchAsync(request)).Success);
        var restored=JsonConvert.DeserializeObject<PackingListManagerSearchRequest>(handler.Body!)!;
        Assert.AreEqual(request.CustomerId,restored.CustomerId);Assert.AreEqual(request.PostingStatus,restored.PostingStatus);
        if(mode==0){Assert.IsNull(restored.CustomerIds);Assert.IsNull(restored.PostingStatuses);}
        else{CollectionAssert.AreEqual(request.CustomerIds,restored.CustomerIds);CollectionAssert.AreEqual(request.PostingStatuses,restored.PostingStatuses);}
        Assert.IsTrue((await new Fixture().Service.SearchAsync(restored)).Success);
    }

    /// <summary>仅模拟 Manager 查询端口，其余方法访问立即失败，避免扩大到详情或来源用例。</summary>
    private sealed class Fixture
    {
        internal PackingListQueryService Service {get;}
        internal bool Ready {get;set;}=true;
        internal int SchemaReads {get;private set;}
        internal int Searches {get;private set;}
        internal PackingListManagerSearchRequest? Request {get;private set;}
        /// <summary>组装真实服务，保留独立查看授权与结构检查的实际执行顺序。</summary>
        internal Fixture(bool allowed=true)
        {
            var session=PurchaseDraftSourceOrchestrationTests.Proxy<IDbSession>.New((method,_)=>throw new AssertFailedException(method.Name));
            var user=PurchaseDraftSourceOrchestrationTests.Proxy<ICurrentUser>.New((method,_)=>method.Name switch
            {"get_IsAuthenticated"=>true,"get_UserId"=>(Guid?)Guid.Parse("d6bfbb81-2874-4113-9a57-501f43caf826"),_=>throw new AssertFailedException(method.Name)});
            var permission=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>
            {Assert.AreEqual("HasPermissionAsync",method.Name);Assert.AreEqual("Shipping.PackingList.View",args[1]);return Task.FromResult(allowed);});
            var repository=PurchaseDraftSourceOrchestrationTests.Proxy<IPackingListQueryRepository>.New((method,args)=>method.Name switch
            {"IsSchemaReadyAsync"=>ReadSchema(),"SearchAsync"=>Search((PackingListManagerSearchRequest)args[0]!),_=>throw new AssertFailedException(method.Name)});
            Service=new(new ServiceBaseContext(session,NullLoggerFactory.Instance),repository,user,permission);
        }
        /// <summary>只观察结构门禁，不连接数据库。</summary>
        private Task<bool> ReadSchema(){SchemaReads++;return Task.FromResult(Ready);}
        /// <summary>只记录实际 Manager 请求，不重新实现筛选或分页。</summary>
        private Task<PagedResponseBase<PackingListManagerListItemDto>> Search(PackingListManagerSearchRequest request)
        {Searches++;Request=request;return Task.FromResult(new PagedResponseBase<PackingListManagerListItemDto>());}
    }

    /// <summary>捕获真实客户端序列化请求并返回确定结果，不向外部发送请求。</summary>
    private sealed class CaptureHandler:HttpMessageHandler
    {
        internal string? Body {get;private set;}
        /// <summary>保留请求正文用于验证集合与旧字段的传输语义。</summary>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Body=await request.Content!.ReadAsStringAsync(token);
            return new(HttpStatusCode.OK){Content=new StringContent("{\"success\":true,\"data\":{\"items\":[],\"totalCount\":0,\"pageIndex\":1,\"pageSize\":20}}",Encoding.UTF8,"application/json")};
        }
    }
}

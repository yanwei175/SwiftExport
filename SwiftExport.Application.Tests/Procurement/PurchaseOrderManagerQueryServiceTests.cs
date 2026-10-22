using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Query;
using SwiftExport.Application.Services.Procurement.PurchaseOrders.Support;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderManagerDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Infrastructure.Data;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>通过真实查询服务和读取支持验证共享查询上限与鉴权顺序，不连接数据库。</summary>
[TestClass]
public sealed class PurchaseOrderManagerQueryServiceTests
{
    /// <summary>关键词、单客户编码和多选编码均接受一百字符，超限不截断且不访问仓储。</summary>
    [TestMethod]
    [DataRow("Keyword",100,true)]
    [DataRow("Keyword",101,false)]
    [DataRow("CustomerCode",100,true)]
    [DataRow("CustomerCode",101,false)]
    [DataRow("CustomerCodes",100,true)]
    [DataRow("CustomerCodes",101,false)]
    public async Task TextBoundaryIsEnforcedWithoutTruncation(string field,int length,bool accepted)
    {
        var request=new PurchaseOrderManagerSearchRequest();
        var text=new string('客',length);
        switch(field)
        {
            case "Keyword": request.Keyword=text; break;
            case "CustomerCode": request.CustomerCode=text; break;
            default: request.CustomerCodes=[text]; break;
        }
        await AssertQueryAsync(request,accepted);
        Assert.AreEqual(text,field switch
        {
            "Keyword"=>request.Keyword,"CustomerCode"=>request.CustomerCode,_=>request.CustomerCodes.Single()
        });
    }

    /// <summary>客户和供应商集合各接受五百个有效项，五百零一项必须在查询前拒绝。</summary>
    [TestMethod]
    [DataRow(false,500,true)]
    [DataRow(false,501,false)]
    [DataRow(true,500,true)]
    [DataRow(true,501,false)]
    public async Task PartyCollectionBoundaryIsEnforced(bool suppliers,int count,bool accepted)
    {
        var request=new PurchaseOrderManagerSearchRequest();
        if(suppliers) request.SupplierIds=Enumerable.Range(0,count).Select(_=>Guid.NewGuid()).ToList();
        else request.CustomerCodes=Enumerable.Range(0,count).Select(i=>$"CUSTOMER-{i}").ToList();
        await AssertQueryAsync(request,accepted);
        Assert.AreEqual(count,suppliers?request.SupplierIds.Count:request.CustomerCodes.Count);
    }

    /// <summary>分页上下界及偏移量溢出保持原有语义，合法大页码可原样传给仓储。</summary>
    [TestMethod]
    [DataRow(1,1,true)]
    [DataRow(2,200,true)]
    [DataRow(int.MaxValue,1,true)]
    [DataRow(1,201,false)]
    [DataRow(0,20,false)]
    [DataRow(1,0,false)]
    [DataRow(int.MaxValue,200,false)]
    public async Task PaginationBoundaryIsEnforced(int pageIndex,int pageSize,bool accepted)
        => await AssertQueryAsync(new(){PageIndex=pageIndex,PageSize=pageSize},accepted);

    /// <summary>无效输入不能绕过身份与读取权限，拒绝时不访问仓储或开启事务。</summary>
    [TestMethod]
    [DataRow(false,true,false)]
    [DataRow(false,true,true)]
    [DataRow(true,false,false)]
    [DataRow(true,false,true)]
    public async Task AuthorizationRefusalPrecedesValidation(bool authenticated,bool allowed,bool invalidInput)
    {
        var fixture=new Fixture(authenticated,allowed);
        var request=new PurchaseOrderManagerSearchRequest{Keyword=new string('x',invalidInput?101:1)};
        var result=await fixture.Service.SearchAsync(request);
        Assert.IsFalse(result.Success);
        Assert.AreEqual(authenticated?ServiceErrorCodes.Forbidden:ServiceErrorCodes.Unauthenticated,result.Error.Code);
        Assert.AreEqual(0,fixture.RepositoryCalls);
        Assert.AreEqual(authenticated?1:0,fixture.PermissionKeys.Count);
        if(authenticated) Assert.AreEqual(PermissionKeys.ProcurementOrderRead,fixture.PermissionKeys.Single());
        Assert.AreEqual(0,fixture.Connection.Begins);
    }

    /// <summary>统一观察真实服务回执、输入和取消令牌传递，确保合法请求没有被复制或缩减。</summary>
    private static async Task AssertQueryAsync(PurchaseOrderManagerSearchRequest request,bool accepted)
    {
        var fixture=new Fixture();
        using var cancellation=new CancellationTokenSource();
        var result=await fixture.Service.SearchAsync(request,cancellation.Token);
        Assert.AreEqual(accepted,result.Success,result.Message);
        Assert.AreEqual(accepted?1:0,fixture.RepositoryCalls);
        Assert.AreEqual(PermissionKeys.ProcurementOrderRead,fixture.PermissionKeys.Single());
        Assert.AreEqual(0,fixture.Connection.Begins);
        if(accepted)
        {
            Assert.AreSame(request,fixture.ObservedRequest);
            Assert.AreEqual(cancellation.Token,fixture.ObservedToken);
            Assert.AreSame(fixture.Page,result.Data);
        }
        else Assert.AreEqual(ServiceErrorCodes.BusinessBlocked,result.Error.Code);
    }

    /// <summary>只替换用户、权限和仓储端口，保留实际服务和读取支持的执行链。</summary>
    private sealed class Fixture
    {
        internal PurchaseOrderManagerQueryService Service {get;}
        internal PurchaseDraftSourceOrchestrationTests.ConnectionStub Connection {get;}=new();
        internal List<string> PermissionKeys {get;}=[];
        internal int RepositoryCalls {get;private set;}
        internal PurchaseOrderManagerSearchRequest? ObservedRequest {get;private set;}
        internal CancellationToken ObservedToken {get;private set;}
        internal PagedResponseBase<PurchaseOrderManagerListItemDto> Page {get;}=new(){PageIndex=1,PageSize=20};

        /// <summary>未使用端口调用立即失败，普通查询不得依赖实际数据库连接或事务。</summary>
        internal Fixture(bool authenticated=true,bool allowed=true)
        {
            var session=(DbSession)RuntimeHelpers.GetUninitializedObject(typeof(DbSession));
            typeof(DbSession).GetField("<Connection>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(session,Connection);
            var user=PurchaseDraftSourceOrchestrationTests.Proxy<ICurrentUser>.New((method,_)=>method.Name switch
            {
                "get_IsAuthenticated"=>authenticated,"get_UserId"=>(Guid?)Guid.Parse("d6bfbb81-2874-4113-9a57-501f43caf826"),
                _=>throw new AssertFailedException(method.Name)
            });
            var permission=PurchaseDraftSourceOrchestrationTests.Proxy<IPermissionEvaluationService>.New((method,args)=>
            {
                Assert.AreEqual("HasPermissionAsync",method.Name);
                PermissionKeys.Add((string)args[1]!);
                return Task.FromResult(allowed);
            });
            var repository=PurchaseDraftSourceOrchestrationTests.Proxy<IPurchaseOrderManagerQueryRepository>.New((method,args)=>
            {
                Assert.AreEqual("SearchAsync",method.Name);
                RepositoryCalls++;
                ObservedRequest=(PurchaseOrderManagerSearchRequest)args[0]!;
                ObservedToken=(CancellationToken)args[1]!;
                return Task.FromResult(Page);
            });
            Service=new(new ProcurementOrderReadSupport(new ServiceBaseContext(session,NullLoggerFactory.Instance),permission,user),repository);
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.ForExcel;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.ForExcel.SalesOrders;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.ForExcel;
namespace SwiftExport.Application.Tests.ForExcel;

/// <summary>检查批量匹配、确认、非标、鉴权和取消，不连接数据库。</summary>
[TestClass]
public sealed class ForExcelSalesOrderTests
{
    /// <summary>唯一匹配返回真实 SKU，输入报价不被覆盖。</summary>
    [TestMethod]
    public async Task MatchAndConfirm()
    {
        var f=new Fixture();var r=f.Request();
        var result=await f.Service.CheckAsync(r);
        Assert.IsTrue(result.Data.CanSave);Assert.AreEqual("REAL",result.Data.Lines[0].Product.Sku);
        Assert.AreEqual(37.7m,r.Lines[0].CnyPrice);
        r.Lines[0].LinkSku="OTHER";f.Existing.Add(new(){Uid=Guid.NewGuid()});
        result=await f.Service.CheckAsync(r);Assert.IsFalse(result.Data.CanSave);
        Assert.AreEqual("LinkSkuMismatch",result.Data.Lines[0].Issues.Single().Code);
        r.Lines[0].AcceptLinkSkuMismatch=true;r.AcceptExistingOrders=true;
        Assert.IsTrue((await f.Service.CheckAsync(r)).Data.CanSave);
        r.Lines[0].Qty=0;Assert.IsFalse((await f.Service.CheckAsync(r)).Data.CanSave);
    }
    /// <summary>空候选、重复关联和停用产品都不静默选择。</summary>
    [TestMethod]
    public async Task FailuresBlockSave()
    {
        foreach(var state in new[]{"NotMatched","Ambiguous","Unavailable"})
        {
            var f=new Fixture();if(state=="NotMatched")f.Products.Clear();
            if(state=="Ambiguous")f.Products.Add(f.Products[0]);
            if(state=="Unavailable")f.Products[0].IsUsable=false;
            var r=await f.Service.CheckAsync(f.Request());Assert.IsFalse(r.Data.CanSave);
            Assert.AreEqual(state,r.Data.Lines[0].MatchStatus);Assert.IsNull(r.Data.Lines[0].Product);
        }
    }
    /// <summary>非标无产品身份，必填描述及单位，数量错误仍阻断。</summary>
    [TestMethod]
    public async Task NonStandard()
    {
        var f=new Fixture();var r=f.Request();r.Lines[0].IsNonStandard=true;
        Assert.IsFalse((await f.Service.CheckAsync(r)).Data.CanSave);
        r.Lines[0].DescriptionEn="非标";r.Lines[0].Unit="PCS";
        var result=await f.Service.CheckAsync(r);Assert.IsTrue(result.Data.CanSave);
        Assert.IsNull(result.Data.Lines[0].Product);Assert.AreEqual(0,f.Skus.Length);
    }
    /// <summary>重复型号一次批量查询，按行标识返回结果。</summary>
    [TestMethod]
    public async Task BatchDeduplicatesKeys()
    {
        var f=new Fixture();var r=f.Request();r.Lines.Add(new(){ClientLineId=Guid.NewGuid(),CustomerSku=" aq ",Qty=1,LinkSku="L"});
        var result=await f.Service.CheckAsync(r);Assert.IsTrue(result.Data.CanSave);
        Assert.AreEqual(2,result.Data.Lines.Count);Assert.AreEqual(1,f.MatchCalls);
        CollectionAssert.AreEqual(new[]{"AQ"},f.Skus);
    }
    /// <summary>未登录和无权限零仓储访问；停用客户不查询产品。</summary>
    [TestMethod]
    public async Task AuthorizationAndCustomer()
    {
        var f=new Fixture();f.IsAuthenticated=false;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated,(await f.Service.CheckAsync(f.Request())).Error.Code);
        f.IsAuthenticated=true;f.Allowed=false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden,(await f.Service.CheckAsync(f.Request())).Error.Code);
        Assert.AreEqual(0,f.CustomerCalls);f.Allowed=true;f.Customer.IsActive=false;
        Assert.IsFalse((await f.Service.CheckAsync(f.Request())).Data.CanSave);Assert.AreEqual(0,f.MatchCalls);
    }
    /// <summary>结构错误在查询前拒绝；单头与数值错误逐项返回。</summary>
    [TestMethod]
    public async Task InvalidInputs()
    {
        var f=new Fixture();var r=f.Request();r.Lines.Add(r.Lines[0]);
        Assert.AreEqual(ServiceErrorCodes.Validation,(await f.Service.CheckAsync(r)).Error.Code);
        r.Lines=[null!];Assert.IsFalse((await f.Service.CheckAsync(r)).Success);
        r.Lines=Enumerable.Range(0,501).Select(_=>new SalesOrderImportLineInput{ClientLineId=Guid.NewGuid()}).ToList();
        Assert.IsFalse((await f.Service.CheckAsync(r)).Success);Assert.AreEqual(0,f.CustomerCalls);
        r=f.Request();r.Header.OrderDate=null;r.Header.ExchangeRate=0;r.Header.Currency="";
        r.Lines[0].Qty=decimal.MaxValue;r.Lines[0].CnyPrice=2;r.Lines[0].GwOfCarton=-1;
        var result=await f.Service.CheckAsync(r);Assert.IsFalse(result.Data.CanSave);
        CollectionAssert.AreEquivalent(new[]{"OrderDateRequired","CurrencyRequired"},result.Data.Issues.Select(i=>i.Code).ToArray());
        CollectionAssert.AreEquivalent(new[]{"NumberNegative","AmountOverflow"},result.Data.Lines[0].Issues.Select(i=>i.Code).ToArray());
    }
    /// <summary>取消不变为通过结果。</summary>
    [TestMethod]
    public async Task Cancellation()
    {
        var f=new Fixture();using var c=new CancellationTokenSource();c.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(()=>f.Service.CheckAsync(f.Request(),c.Token));
        Assert.AreEqual(0,f.CustomerCalls);
    }
    /// <summary>显式选择可消除多候选歧义，但外部身份不能越过客户型号范围。</summary>
    [TestMethod]
    public async Task ExplicitSelectionIsScoped()
    {
        var f=new Fixture();var other=new SalesOrderImportProductCandidateDto{CustomerProductId=Guid.NewGuid(),ProductId=Guid.NewGuid(),CustomerSku="AQ",Sku="SECOND",LinkSku="L",Unit="PCS",IsUsable=true};
        f.Products.Add(other);var request=f.Request();request.Lines[0].SelectedCustomerProductId=other.CustomerProductId;
        var result=await f.Service.CheckAsync(request);Assert.IsTrue(result.Data.CanSave);Assert.AreEqual("SECOND",result.Data.Lines[0].Product.Sku);
        request.Lines[0].SelectedCustomerProductId=Guid.NewGuid();
        result=await f.Service.CheckAsync(request);Assert.IsFalse(result.Data.CanSave);Assert.AreEqual("SelectionInvalid",result.Data.Lines[0].Issues.Single().Code);
    }
    /// <summary>缺失、零和旧负汇率不阻断检查，也不改写币种或双币种单价。</summary>
    [TestMethod]
    public async Task DeprecatedExchangeRateDoesNotBlockCheckOrConvertPrices()
    {
        foreach (var rate in new decimal?[] { null, 0m, -7m, 7.2m })
        {
            var f = new Fixture(); var request = f.Request();
            request.Header.ExchangeRate = rate; request.Lines[0].UsdPrice = 5.18m;
            var result = await f.Service.CheckAsync(request);
            Assert.IsTrue(result.Success, result.Message); Assert.IsTrue(result.Data.CanSave);
            Assert.AreEqual(0, result.Data.Issues.Count);
            Assert.AreEqual(rate, request.Header.ExchangeRate); Assert.AreEqual("CNY", request.Header.Currency);
            Assert.AreEqual(37.7m, request.Lines[0].CnyPrice); Assert.AreEqual(5.18m, request.Lines[0].UsdPrice);
        }
    }

    private sealed class Fixture:IForExcelSalesOrderRepository,ICurrentUser,IPermissionEvaluationService
    {
        internal int CustomerCalls,MatchCalls;internal string[] Skus=[];internal bool Allowed=true;
        internal SalesOrderImportCustomerDto Customer=new(){Uid=Guid.NewGuid(),CustomerCode="C",IsActive=true};
        internal List<SalesOrderImportProductCandidateDto> Products=[new(){CustomerProductId=Guid.NewGuid(),ProductId=Guid.NewGuid(),CustomerSku="AQ",Sku="REAL",LinkSku="L",Unit="PCS",IsUsable=true}];
        internal List<SalesOrderImportExistingOrderDto> Existing=[];
        internal ForExcelSalesOrderService Service=>new(this,this,this);
        internal CheckSalesOrderImportRequest Request()=>new(){CustomerCode="C",Header=new(){OrderDate=DateTime.Today,Currency="CNY",ExchangeRate=1,InvoiceNo="47"},Lines=[new(){ClientLineId=Guid.NewGuid(),CustomerSku="AQ",LinkSku="L",Qty=64,CnyPrice=37.7m}]};
        public Task<IReadOnlyList<SalesOrderImportCustomerDto>> FindCustomersAsync(string code,CancellationToken token){CustomerCalls++;return Task.FromResult<IReadOnlyList<SalesOrderImportCustomerDto>>([Customer]);}
        public Task<IReadOnlyList<SalesOrderImportProductCandidateDto>> MatchProductsAsync(Guid id,string[] skus,CancellationToken token){MatchCalls++;Skus=skus;return Task.FromResult<IReadOnlyList<SalesOrderImportProductCandidateDto>>(Products);}
        public Task<IReadOnlyList<SalesOrderImportExistingOrderDto>> FindExistingOrdersAsync(Guid id,string invoice,string po,CancellationToken token)=>Task.FromResult<IReadOnlyList<SalesOrderImportExistingOrderDto>>(Existing);
        public string UserName=>"test";public Guid? UserId=>Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");public bool IsAuthenticated{get;set;}=true;
        public Task<bool> HasPermissionAsync(Guid id,string key,CancellationToken token=default)=>Task.FromResult(Allowed);
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid id,CancellationToken token=default)=>throw new NotSupportedException();
    }
}

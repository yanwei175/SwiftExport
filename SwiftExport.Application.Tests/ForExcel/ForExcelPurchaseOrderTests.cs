using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.ForExcel;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Models.Auth.Permissions.Evaluation;
using SwiftExport.Contracts.DTOs.ForExcel.PurchaseOrders;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.ForExcel;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Contracts.Wrappers;
namespace SwiftExport.Application.Tests.ForExcel;

/// <summary>采购检查纯托管回归：匹配、非标、鉴权、确认与取消；不连接数据库。</summary>
[TestClass]
public sealed class ForExcelPurchaseOrderTests
{
    /// <summary>BONO 重复只阻断保存，仍返回可用产品；空号不参与占号。</summary>
    [TestMethod]
    public async Task DuplicateBoNoBlocksSaveWithoutDiscardingProductMatch()
    {
        var f = new Fixture { DuplicateBoNo = true }; var request = f.Request();
        var result = await f.Service.CheckAsync(request);
        Assert.IsFalse(result.Data.CanSave); Assert.AreEqual("REAL-SKU", result.Data.Lines[0].Product.Sku);
        var issue = result.Data.Issues.Single(x => x.Code == PurchaseOrderImportIssueCodes.BoNoAlreadyExists);
        Assert.AreEqual("BoNo", issue.Field); Assert.IsFalse(issue.RequiresConfirmation);
        request.Header.BoNo = "   "; Assert.IsTrue((await f.Service.CheckAsync(request)).Data.CanSave);
    }
    /// <summary>没有供应商的独立 SKU 检查仍返回产品，仅整单资格被拒绝。</summary>
    [TestMethod]
    public async Task MissingSupplierStillReturnsProductResult()
    {
        var f = new Fixture(); var request = f.Request(); request.SupplierId = Guid.Empty;
        var result = await f.Service.CheckAsync(request);
        Assert.IsFalse(result.Data.CanSave); Assert.AreEqual("Matched", result.Data.Lines[0].MatchStatus);
        Assert.AreEqual("REAL-SKU", result.Data.Lines[0].Product.Sku);
    }
    /// <summary>精确客户型号返回真实 SKU，并保持报价、配置、优惠和 BoNo 输入原样。</summary>
    [TestMethod]
    public async Task MatchesCustomerSkuWithoutOverwritingSnapshots()
    {
        var f = new Fixture(); var request = f.Request();
        request.Header.DiscountRate = 0.95m; request.Header.DiscountAmount = 100;
        var result = await f.Service.CheckAsync(request);
        Assert.IsTrue(result.Data.CanSave);
        Assert.AreEqual("REAL-SKU",result.Data.Lines[0].Product.Sku);
        Assert.AreEqual(116m,request.Lines[0].UnitPrice);
        Assert.AreEqual("Excel配置",request.Lines[0].FeaturesZh);
        Assert.AreEqual("AT260820DQ",request.Header.BoNo);
        Assert.AreEqual(0.95m,request.Header.DiscountRate);
        Assert.AreEqual(100m,request.Header.DiscountAmount);
    }

    /// <summary>空候选、多候选与停用候选都阻断，不偷偷选择第一页或第一个。</summary>
    [TestMethod]
    public async Task MissingAmbiguousAndInactiveProductsBlock()
    {
        foreach (var status in new[] { "NotMatched","Ambiguous","Unavailable" })
        {
            var f = new Fixture();
            if (status == "NotMatched") f.Products.Clear();
            if (status == "Ambiguous") f.Products.Add(new() { CustomerProductId=Guid.NewGuid(),CustomerSku="AQ",IsUsable=true });
            if (status == "Unavailable") f.Products[0].IsUsable=false;
            var result = await f.Service.CheckAsync(f.Request());
            Assert.IsFalse(result.Data.CanSave); Assert.AreEqual(status,result.Data.Lines[0].MatchStatus);
            Assert.IsNull(result.Data.Lines[0].Product);
        }
    }

    /// <summary>候选选择受客户型号约束；型号差异只能由明确确认解除。</summary>
    [TestMethod]
    public async Task ExplicitSelectionAndMismatchConfirmation()
    {
        var f = new Fixture(); var request=f.Request();
        f.Products.Add(new() { CustomerProductId=Guid.NewGuid(),CustomerSku="AQ",ProductId=Guid.NewGuid(),Sku="SECOND",Unit="PCS",LinkSku="L",IsUsable=true });
        request.Lines[0].SelectedCustomerProductId=f.Products[1].CustomerProductId;
        request.Lines[0].LinkSku="Excel原型号";
        var result=await f.Service.CheckAsync(request);
        Assert.IsFalse(result.Data.CanSave); Assert.AreEqual("SECOND",result.Data.Lines[0].Product.Sku);
        Assert.AreEqual("LinkSkuMismatch",result.Data.Lines[0].Issues.Single().Code);
        request.Lines[0].AcceptLinkSkuMismatch=true;
        Assert.IsTrue((await f.Service.CheckAsync(request)).Data.CanSave);
        request.Lines[0].SelectedCustomerProductId=Guid.NewGuid();
        Assert.IsFalse((await f.Service.CheckAsync(request)).Data.CanSave);
    }

    /// <summary>非标显式标记、描述和启用单位必填；标准未匹配不能自动变非标。</summary>
    [TestMethod]
    public async Task NonStandardRequiresExplicitValidSnapshot()
    {
        var f=new Fixture();f.Products.Clear();var request=f.Request();
        Assert.IsFalse((await f.Service.CheckAsync(request)).Data.CanSave);
        request.Lines[0].IsNonStandard=true;
        Assert.IsFalse((await f.Service.CheckAsync(request)).Data.CanSave);
        request.Lines[0].DescriptionZh="非标配件";request.Lines[0].Unit="PCS";
        var result=await f.Service.CheckAsync(request);
        Assert.IsTrue(result.Data.CanSave);Assert.IsNull(result.Data.Lines[0].Product);
        Assert.AreEqual("NonStandard",result.Data.Lines[0].MatchStatus);
        f.UnitEnabled=false;Assert.IsFalse((await f.Service.CheckAsync(request)).Data.CanSave);
    }

    /// <summary>无权限零仓储访问，客户或供应商停用都会阻断保存。</summary>
    [TestMethod]
    public async Task AuthorizationAndPartyValidation()
    {
        var f=new Fixture();f.IsAuthenticated=false;
        Assert.AreEqual(ServiceErrorCodes.Unauthenticated,(await f.Service.CheckAsync(f.Request())).Error.Code);
        f.IsAuthenticated=true;f.Allowed=false;
        Assert.AreEqual(ServiceErrorCodes.Forbidden,(await f.Service.CheckAsync(f.Request())).Error.Code);
        Assert.AreEqual(0,f.Calls);
        f.Allowed=true;f.Customer.IsActive=false;
        Assert.IsFalse((await f.Service.CheckAsync(f.Request())).Data.CanSave);
        f.Customer.IsActive=true;f.Supplier.IsActive=false;
        Assert.IsFalse((await f.Service.CheckAsync(f.Request())).Data.CanSave);
    }

    /// <summary>空价、负价、数量精度、物理单价精度、溢出及文本超长都不能通过。</summary>
    [TestMethod]
    public async Task NumericAndTextErrorsBlock()
    {
        foreach (var change in new Action<PurchaseOrderImportLineInput>[]
        {
            x=>x.UnitPrice=null,x=>x.UnitPrice=-1,x=>x.Quantity=0,x=>x.Quantity=1.00001m,
            x=>x.UnitPrice=1.00001m,x=>{x.Quantity=99999999999999m;x.UnitPrice=2;},
            x=>x.FeaturesZh=new string('x',501)
        })
        {
            var f=new Fixture();var request=f.Request();change(request.Lines[0]);
            Assert.IsFalse((await f.Service.CheckAsync(request)).Data.CanSave);
        }
        var valid=new Fixture();var zero=valid.Request();zero.Lines[0].UnitPrice=0;
        Assert.IsTrue((await valid.Service.CheckAsync(zero)).Data.CanSave);
        zero.Header.DiscountRate=1000;
        Assert.IsFalse((await valid.Service.CheckAsync(zero)).Data.CanSave);
    }

    /// <summary>重复型号只发一次批量查询；结构错误在查询前拒绝。</summary>
    [TestMethod]
    public async Task BatchAndStructure()
    {
        var f=new Fixture();var request=f.Request();
        request.Lines.Add(new() {ClientLineId=Guid.NewGuid(),CustomerSku=" aq ",Quantity=1,UnitPrice=0});
        Assert.IsTrue((await f.Service.CheckAsync(request)).Data.CanSave);
        CollectionAssert.AreEqual(new[]{"AQ"},f.Skus);Assert.AreEqual(1,f.MatchCalls);
        var bad=new Fixture();var duplicate=bad.Request();duplicate.Lines.Add(duplicate.Lines[0]);
        Assert.AreEqual(ServiceErrorCodes.Validation,(await bad.Service.CheckAsync(duplicate)).Error.Code);
        Assert.AreEqual(0,bad.Calls);
    }

    /// <summary>取消向上层传播；客供件必须提供有效送达供应商。</summary>
    [TestMethod]
    public async Task CancellationAndCustomerSuppliedDelivery()
    {
        var f=new Fixture();using var c=new CancellationTokenSource();c.Cancel();
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(()=>f.Service.CheckAsync(f.Request(),c.Token));
        Assert.AreEqual(0,f.Calls);
        f.Products[0].MaterialType="客供件";var request=f.Request();
        Assert.IsFalse((await f.Service.CheckAsync(request)).Data.CanSave);
        request.Lines[0].DeliverySupplierId=f.Supplier.Uid;
        Assert.IsTrue((await f.Service.CheckAsync(request)).Data.CanSave);
    }

    /// <summary>窄仓储和权限替身只观察当前用例，不模拟 SQL Server 事务。</summary>
    private sealed class Fixture : IForExcelPurchaseOrderRepository,ICurrentUser,IPermissionEvaluationService,IProcurementOrderDocumentRepository
    {
        internal int Calls,MatchCalls;internal string[] Skus=[];
        internal bool Allowed=true,UnitEnabled=true,DuplicateBoNo;
        internal PurchaseOrderImportCustomerDto Customer=new(){Uid=Guid.NewGuid(),CustomerCode="C",IsActive=true};
        internal PurchaseOrderImportSupplierDto Supplier=new(){Uid=Guid.NewGuid(),SupplierCode="S",SupplierName="供应商",IsActive=true};
        internal List<PurchaseOrderImportProductCandidateDto> Products=[new(){CustomerProductId=Guid.NewGuid(),ProductId=Guid.NewGuid(),CustomerSku="AQ",Sku="REAL-SKU",LinkSku="L",Unit="PCS",IsUsable=true}];
        internal ForExcelPurchaseOrderService Service=>new(this,this,this,this);
        public Task<bool> BoNoExistsAsync(string? boNo,CancellationToken token)
        { token.ThrowIfCancellationRequested();Calls++;return Task.FromResult(DuplicateBoNo && !string.IsNullOrEmpty(boNo?.Trim(' '))); }
        public Task<PurchaseOrderHead?> GetHeadAsync(Guid uid,CancellationToken ct)=>throw new NotSupportedException();
        public Task<List<PurchaseOrderLine>> GetLinesAsync(Guid uid,CancellationToken ct)=>throw new NotSupportedException();
        public Task<RepositoryWriteResult> InsertAsync(PurchaseOrderHead h,IReadOnlyList<PurchaseOrderLine> l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<RepositoryWriteResult> SaveAsync(PurchaseOrderHead h,IReadOnlyList<PurchaseOrderLine> l,CancellationToken ct)=>throw new NotSupportedException();
        public Task<RepositoryWriteResult> ConfirmAsync(PurchaseOrderHead h,CancellationToken ct)=>throw new NotSupportedException();
        internal CheckPurchaseOrderImportRequest Request()=>new(){CustomerCode="C",SupplierId=Supplier.Uid,
            Header=new(){OrderDate=DateTime.Today,BoNo="AT260820DQ"},
            Lines=[new(){ClientLineId=Guid.NewGuid(),CustomerSku="AQ",LinkSku="L",Quantity=36,UnitPrice=116,FeaturesZh="Excel配置"}]};
        public Task<IReadOnlyList<PurchaseOrderImportCustomerDto>> FindCustomersAsync(string code,CancellationToken token)
        {Calls++;return Task.FromResult<IReadOnlyList<PurchaseOrderImportCustomerDto>>([Customer]);}
        public Task<PurchaseOrderImportSupplierDto?> FindSupplierAsync(Guid uid,CancellationToken token)
        {Calls++;return Task.FromResult<PurchaseOrderImportSupplierDto?>(uid==Supplier.Uid?Supplier:null);}
        public Task<IReadOnlyList<PurchaseOrderImportProductCandidateDto>> MatchProductsAsync(Guid id,string[] skus,CancellationToken token)
        {Calls++;MatchCalls++;Skus=skus;return Task.FromResult<IReadOnlyList<PurchaseOrderImportProductCandidateDto>>(Products);}
        public Task<string?> FindUnitAsync(string unit,CancellationToken token)
        {Calls++;return Task.FromResult<string?>(UnitEnabled?"PCS":null);}
        public string UserName=>"test";public Guid? UserId=>Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        public bool IsAuthenticated{get;set;}=true;
        public Task<bool> HasPermissionAsync(Guid id,string key,CancellationToken token=default)=>Task.FromResult(Allowed);
        public Task<UserPermissionProjection?> GetProjectionAsync(Guid id,CancellationToken token=default)=>throw new NotSupportedException();
    }
}

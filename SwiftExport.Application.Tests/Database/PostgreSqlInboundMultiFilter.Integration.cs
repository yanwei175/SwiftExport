using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Query;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用已授权隔离库和单事务回滚夹具，验证入库集合条件在真实 SQL 分页前生效。</summary>
public sealed partial class PostgreSqlInboundQueryMigrationTests
{
    /// <summary>类型与状态集合内并集、集合间交集；空选覆盖旧单值，null 回退旧单值，计数和分页一致。</summary>
    [TestMethod,TestCategory("PostgreSqlInboundMultiFilterIntegration")]
    public Task RealPg_MultiFiltersPreserveUnionFallbackAndPaging()=>Rollback(async f=>
    {
        var definitions=new[]
        {
            (InboundDocumentTypeEnum.PurchaseReceipt,InboundPostingStatusEnum.Unposted),
            (InboundDocumentTypeEnum.PurchaseReceipt,InboundPostingStatusEnum.Posted),
            (InboundDocumentTypeEnum.PurchaseReceipt,InboundPostingStatusEnum.Reversed),
            (InboundDocumentTypeEnum.ProductionReceipt,InboundPostingStatusEnum.Unposted),
            (InboundDocumentTypeEnum.ProductionReceipt,InboundPostingStatusEnum.Reversed),
            (InboundDocumentTypeEnum.OpeningBalance,InboundPostingStatusEnum.Posted)
        };
        var expected=new List<Guid>();
        for(var index=0;index<definitions.Length;index++)
        {
            var (type,status)=definitions[index];var head=f.Head("multi-"+index);
            head.DocumentType=type;head.PostingStatus=status;
            if(status!=InboundPostingStatusEnum.Unposted)head.PostedDate=f.Today;
            if(status==InboundPostingStatusEnum.Reversed){head.ReversedDate=f.Today;head.ReverseReason="集合查询测试";}
            await f.Add(head);
            if(type is InboundDocumentTypeEnum.PurchaseReceipt or InboundDocumentTypeEnum.ProductionReceipt
                && status is InboundPostingStatusEnum.Unposted or InboundPostingStatusEnum.Reversed)expected.Add(head.Uid);
        }
        var request=new InboundManagerSearchRequest
        {
            Keyword=f.Prefix,PageSize=2,DocumentType=InboundDocumentTypeEnum.OpeningBalance,PostingStatus=InboundPostingStatusEnum.Posted,
            DocumentTypes=[InboundDocumentTypeEnum.PurchaseReceipt,InboundDocumentTypeEnum.ProductionReceipt],
            PostingStatuses=[InboundPostingStatusEnum.Unposted,InboundPostingStatusEnum.Reversed]
        };
        var actual=new List<Guid>();
        for(var page=1;page<=3;page++)
        {
            request.PageIndex=page;var result=await f.Repository.SearchAsync(request,default);
            Assert.AreEqual(4,result.TotalCount);Assert.HasCount(page<=2?2:0,result.Items);
            actual.AddRange(result.Items.Select(row=>row.Uid));
        }
        CollectionAssert.AreEquivalent(expected,actual);
        request.PageIndex=1;request.PageSize=200;request.DocumentTypes=[];request.PostingStatuses=[];
        Assert.AreEqual(6,(await f.Repository.SearchAsync(request,default)).TotalCount,"空集合必须忽略仍存在的旧单值。");
        request.DocumentTypes=null!;request.PostingStatuses=null!;
        var legacy=await f.Repository.SearchAsync(request,default);
        Assert.AreEqual(1,legacy.TotalCount);Assert.AreEqual(InboundDocumentTypeEnum.OpeningBalance,legacy.Items.Single().DocumentType);
        request.DocumentTypes=[InboundDocumentTypeEnum.ProductionReceipt];request.PostingStatus=InboundPostingStatusEnum.Unposted;
        Assert.AreEqual(1,(await f.Repository.SearchAsync(request,default)).TotalCount);
        request.DocumentTypes=null!;request.DocumentType=InboundDocumentTypeEnum.PurchaseReceipt;request.PostingStatuses=[InboundPostingStatusEnum.Reversed];
        Assert.AreEqual(1,(await f.Repository.SearchAsync(request,default)).TotalCount);
        request.DocumentTypes=[InboundDocumentTypeEnum.PurchaseReceipt,InboundDocumentTypeEnum.PurchaseReceipt];
        request.PostingStatuses=[InboundPostingStatusEnum.Unposted,InboundPostingStatusEnum.Unposted];
        Assert.AreEqual(1,(await f.Repository.SearchAsync(request,default)).TotalCount,"重复枚举不应重复返回单头。");
        Assert.AreSame(f.Transaction,f.Session.Transaction);
    });
}

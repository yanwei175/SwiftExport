using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Sources;
using SwiftExport.Contracts.SysEnums.Inventory;
using SwiftExport.Core.Common;

namespace SwiftExport.Application.Tests.Database;

/// <summary>借用既有无网络 ADO 夹具，执行真实来源仓储并验证范围绑定、整头证明和分页边界。</summary>
public sealed partial class PostgreSqlInboundQueryMigrationTests
{
    /// <summary>快照与正式容量读取都将请求采购行以 uuid 数组传入同一整头证明，拒绝时不读投影。</summary>
    [TestMethod]
    [DataRow(false)] [DataRow(true)]
    public async Task Offline_SourceFactsUseOnlyRequestedIds(bool posting)
    {
        using var f=new ReadFixture(true);var id=Guid.NewGuid();using var cancellation=new CancellationTokenSource();
        f.Connection.Results=[posting?Rows(new InboundPurchasePostingSourceDto{SourceLineId=id}):Rows(new InboundSourceCandidateDto{SourceLineId=id})];
        if(posting)Assert.AreEqual(id,(await f.Sources.ReadPostingSourcesAsync([id],cancellation.Token)).Single().SourceLineId);
        else Assert.AreEqual(id,(await f.Sources.GetSourcesAsync([id],cancellation.Token)).Single().SourceLineId);
        Assert.AreEqual(2,f.Connection.Commands);
        CollectionAssert.AreEqual(new[]{id},(Guid[])f.Connection.Values["Ids"]!);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,f.Connection.Types["Ids"]);
        foreach(var sql in f.Connection.Sqls)
        {
            StringAssert.Contains(sql,"receipt_scope AS (SELECT unnest(@Ids) AS uid)");
            StringAssert.Contains(sql,"receipt_head_reviews");
            StringAssert.Contains(sql,"h.document_type IN(11,12)");
        }
        Assert.AreEqual(cancellation.Token,f.Connection.Token);Assert.AreSame(f.Transaction,f.Session.Transaction);
        f.Connection.InvalidHistory=true;
        if(posting)await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Sources.ReadPostingSourcesAsync([id],default));
        else await Assert.ThrowsAsync<UnverifiedReceiptHistoryException>(()=>f.Sources.GetSourcesAsync([id],default));
        Assert.AreEqual(3,f.Connection.Commands);Assert.AreEqual(0,f.Connection.EndTransactions);
    }

    /// <summary>空来源集合不访问数据库，不再触发与请求无关的全局未知历史检查。</summary>
    [TestMethod]
    public async Task Offline_EmptySourceScopeDoesNotReadHistory()
    {
        using var f=new ReadFixture(false);f.Connection.InvalidHistory=true;
        Assert.IsEmpty(await f.Sources.GetSourcesAsync([],default));Assert.IsEmpty(await f.Sources.ReadPostingSourcesAsync([],default));
        Assert.IsFalse(await f.Sources.HasUnprovenReceiptAsync([],default));Assert.AreEqual(0,f.Connection.Commands);
    }

    /// <summary>候选在计数与分页前排除异常行，使用单次物化查询，不执行会阻断整页的全局标量检查。</summary>
    [TestMethod]
    public async Task Offline_SourceCandidatesFilterHistoryBeforeCountAndPaging()
    {
        using var f=new ReadFixture(false);var id=Guid.NewGuid();
        var rows=Rows(new InboundSourceCandidateDto{SourceLineId=id});
        rows.Columns.Add("TotalCount",typeof(int)).SetOrdinal(0);rows.Rows[0]["TotalCount"]=9;
        f.Connection.Results=[rows];f.Connection.InvalidHistory=true;
        var result=await f.Sources.SearchSourcesAsync(new(){PurchaseCategory=InboundPurchaseCategoryEnum.Normal,PageIndex=2,PageSize=1},default);
        Assert.AreEqual(9,result.TotalCount);Assert.AreEqual(id,result.Items.Single().SourceLineId);
        Assert.AreEqual(1,f.Connection.Commands);
        var sql=f.Connection.Sql;
        Assert.IsTrue(sql.IndexOf("COALESCE(received.has_unit_mismatch,0)=0",StringComparison.Ordinal)<sql.IndexOf("page AS MATERIALIZED",StringComparison.Ordinal));
        StringAssert.Contains(sql,"COUNT(*)::int AS \"TotalCount\" FROM candidates");
        Assert.AreEqual(1L,f.Connection.Values["Skip"]);Assert.AreEqual(1,f.Connection.Values["Take"]);
    }
}

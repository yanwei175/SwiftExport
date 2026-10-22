using System.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Infrastructure.Repositories.Shipping;

namespace SwiftExport.Application.Tests.Database;

/// <summary>复用无网络 ADO 夹具执行真实 Manager SQL，验证数组绑定与计数分页协议，不模拟业务筛选算法。</summary>
public sealed partial class PostgreSqlRepositoryTests
{
    /// <summary>客户 uuid[] 与状态 integer[] 保留优先级；计数和页共用 ANY 条件，日期及发票摘要不变。</summary>
    [TestMethod,DataRow(0),DataRow(1),DataRow(2),DataRow(3),DataRow(4)]
    public async Task PackingListManagerFilter_BindsEffectiveArraysBeforeCountAndPage(int mode)
    {
        using var f=new Fixture();var repository=new PostgreSqlPackingListRepository(DictionaryContext(f));
        var first=Guid.NewGuid();var second=Guid.NewGuid();var legacy=Guid.NewGuid();
        var request=new PackingListManagerSearchRequest{CustomerId=legacy,PostingStatus=PackingListPostingStatusEnum.Posted,
            PageIndex=2,PageSize=2,InvoiceDateFrom=new(2026,10,21,12,30,0),InvoiceDateTo=new(2026,10,22,19,0,0)};
        if(mode==1){request.CustomerIds=[];request.PostingStatuses=[];}
        if(mode>=2)request.CustomerIds=[first,second];
        if(mode==2||mode==4)request.PostingStatuses=[PackingListPostingStatusEnum.Unposted,PackingListPostingStatusEnum.Reversed];
        if(mode==4)request.CustomerIds=null!;
        var row=new PackingListManagerListItemDto{Uid=Guid.NewGuid(),CustomerId=first,PackingListNo="PL-MULTI"};
        var count=new DataTable();count.Columns.Add("Count",typeof(int));count.Rows.Add(4);
        f.Connection.ResultSets=[count,PackingManagerRows(row)];
        var result=await repository.SearchAsync(request);
        Assert.AreEqual(4,result.TotalCount);Assert.AreEqual(row.Uid,result.Items.Single().Uid);
        CollectionAssert.AreEqual(mode==1?[]:mode==0||mode==4?new[]{legacy}:new[]{first,second},(Guid[])f.Connection.Values["CustomerIds"]!);
        CollectionAssert.AreEqual(mode==1?[]:mode==2||mode==4?new[]{0,8}:new[]{1},(int[])f.Connection.Values["PostingStatuses"]!);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Uuid,f.Connection.Types["CustomerIds"]);
        Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,f.Connection.Types["PostingStatuses"]);
        Assert.AreEqual(new DateTime(2026,10,21),f.Connection.Values["DateFrom"]);Assert.AreEqual(new DateTime(2026,10,22),f.Connection.Values["DateTo"]);
        Assert.AreEqual(2L,f.Connection.Values["Skip"]);Assert.AreEqual(2,f.Connection.Values["Take"]);
        var sql=f.Connection.Text;
        Assert.AreEqual(2,sql.Split("h.customer_id=ANY(@CustomerIds)").Length-1);
        Assert.AreEqual(2,sql.Split("h.posting_status=ANY(@PostingStatuses)").Length-1);
        StringAssert.Contains(sql,"h.is_deleted=FALSE");StringAssert.Contains(sql,"h.invoice_date<@DateTo+INTERVAL '1 day'");
        StringAssert.Contains(sql,"AS \"InvoiceNosSummary\"");StringAssert.Contains(sql,"ORDER BY h.invoice_date DESC NULLS LAST,ARRAY[");
        Assert.AreSame(f.Session.Transaction,f.Connection.LastTransaction);Assert.AreEqual(0,f.Connection.Commits);Assert.AreEqual(0,f.Connection.Rollbacks);
    }

    /// <summary>只提供 DTO 对应的真实列结构和测试数据，让 Dapper 完成映射，不在夹具重算筛选结果。</summary>
    private static DataTable PackingManagerRows(PackingListManagerListItemDto row)
    {
        var table=new DataTable();var properties=typeof(PackingListManagerListItemDto).GetProperties();
        foreach(var property in properties)
        {
            var type=Nullable.GetUnderlyingType(property.PropertyType)??property.PropertyType;
            table.Columns.Add(property.Name,type.IsEnum?typeof(int):type);
        }
        table.Rows.Add(properties.Select(property=>property.GetValue(row) is Enum value?Convert.ToInt32(value):property.GetValue(row)??DBNull.Value).ToArray());
        return table;
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.SysEnums;
namespace SwiftExport.Application.Tests.OrderTracking.Overview;

/// <summary>验证全来源子表没有分页，以及仍由主表承担的参数化、筛选和来源分页。</summary>
[TestClass]
public sealed class OrderTrackingOverviewRepositoryTests
{
    /// <summary>超过两百条仍完整返回，主表字段完全不出现在子表条件，借用外层事务。</summary>
    [TestMethod]
    public async Task FullSource_ReturnsEveryRowWithoutPaging()
    {
        using var f=new OrderTrackingReadFixture(true);var source=Guid.NewGuid();
        var rows=Enumerable.Range(0,301).Select(index=>(object)new OrderTrackingListItemDto{GrossDemandId=Guid.NewGuid(),SourceType="SO",SourceUid=source,
            SourceLineId=Guid.NewGuid(),SourceNo="同一来源",DemandQty=index,Unit=index==0?null!:"PCS",
            LifecycleStatus=index%2==0?GrossDemandLifecycleStatusEnum.Active:GrossDemandLifecycleStatusEnum.Closed}).ToArray();
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(rows));
        using var cancellation=new CancellationTokenSource();
        var result=await f.Overview.GetSourceDemandsAsync("SO",source,cancellation.Token);
        Assert.HasCount(301,result.Items);Assert.AreEqual(source,result.SourceGroupId);Assert.AreEqual("SO",result.SourceType);
        var c=f.Connection.Commands.Single();Assert.AreEqual(source,c.Value("SourceGroupId"));Assert.AreEqual("SO",c.Value("SourceType"));
        foreach(var forbidden in new[]{"LIMIT","OFFSET","@Keyword","@LifecycleStatuses","@RequiredDate","net_demand","purchase_order","packing_list","FOR UPDATE"})
            Assert.IsFalse(c.Sql.Contains(forbidden,StringComparison.OrdinalIgnoreCase),forbidden);
        Assert.AreSame(f.Session.Transaction,c.Transaction);Assert.AreEqual(cancellation.Token,f.Connection.Token);
        Assert.AreEqual(1,f.Connection.Begins);Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);
        StringAssert.Contains(c.Sql,"g.source_line_number ASC NULLS LAST,g.uid");
    }

    /// <summary>完整映射快照、客户及来源缺失，空单位和交期不补默认值。</summary>
    [TestMethod]
    public async Task FullSource_MapsEveryFieldAndPreservesNulls()
    {
        using var f=new OrderTrackingReadFixture();
        var expected=new OrderTrackingListItemDto{GrossDemandId=Guid.NewGuid(),Version=7,SourceType="MD",SourceUid=Guid.Empty,SourceLineId=Guid.NewGuid(),
            SourceNo="原始来源",CustomerId=null,CustomerCode=null!,CustomerName=null!,CustomerPo=null!,InvoiceNo=null!,ProductId=null,
            IsNonStandard=true,Sku=" 快照 ",CustomerSku="客户型号",LinkSku="中性",ImagePath="图片",Description="描述",Color="红",Specification="规格",
            DemandQty=12.25m,Unit=null!,RequiredDate=null,LifecycleStatus=GrossDemandLifecycleStatusEnum.Voided};
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(expected));
        var actual=(await f.Overview.GetSourceDemandsAsync("MD",expected.GrossDemandId)).Items.Single();
        foreach(var property in typeof(OrderTrackingListItemDto).GetProperties())
        {
            Assert.AreEqual(property.GetValue(expected),property.GetValue(actual),property.Name);
            StringAssert.Contains(f.Connection.Commands.Single().Sql,"AS \""+property.Name+"\"");
        }
        var sql=f.Connection.Commands.Single().Sql;StringAssert.Contains(sql,"LEFT JOIN public.customer");
        StringAssert.Contains(sql,"LEFT JOIN public.sales_order_header");StringAssert.Contains(sql,"COALESCE(NULLIF(g.source_uid");
    }

    /// <summary>主表仍按来源组分页，超末页保留组计数，偏移用long避免溢出。</summary>
    [TestMethod]
    [DataRow(-1,0,1,20)]
    [DataRow(2,999,2,200)]
    [DataRow(int.MaxValue,20,int.MaxValue,20)]
    public async Task SourcePaging_PreservesCountAndLongOffset(int index,int size,int expectedIndex,int expectedSize)
    {
        using var f=new OrderTrackingReadFixture(true);f.Page(11);
        var result=await f.Sources.SearchSourcesAsync(new(){PageIndex=index,PageSize=size},OrderTrackingReadFixture.BusinessDate);
        Assert.AreEqual(11,result.TotalCount);Assert.HasCount(0,result.Items);Assert.AreEqual(expectedIndex,result.PageIndex);Assert.AreEqual(expectedSize,result.PageSize);
        var c=f.Connection.Commands[1];Assert.AreEqual((long)(expectedIndex-1)*expectedSize,c.Value("Offset"));Assert.AreEqual(expectedSize,c.Value("PageSize"));
        foreach(var command in f.Connection.Commands)Assert.AreSame(f.Session.Transaction,command.Transaction);
    }

    /// <summary>六字段OR仍与多选/日期/状态取交集，字面符号、参数类型及请求原文保持。</summary>
    [TestMethod]
    public async Task SourceFilters_BindTypedArraysAndLiteralKeyword()
    {
        using var f=new OrderTrackingReadFixture();f.Page(0);
        var day=new DateTime(2026,10,19,23,50,0,DateTimeKind.Utc);
        var request=new OrderTrackingSourceSearchRequest{SourceTypes=["SO","CR","SO"],CustomerCodes=[" A ","a","B"," "],
            Keyword=" a b?%_~';DROP ",LifecycleStatuses=[GrossDemandLifecycleStatusEnum.Closed,GrossDemandLifecycleStatusEnum.Active,GrossDemandLifecycleStatusEnum.Closed],
            RequiredDateFrom=day,RequiredDateTo=DateTime.MaxValue};
        await f.Sources.SearchSourcesAsync(request,OrderTrackingReadFixture.BusinessDate);
        foreach(var c in f.Connection.Commands)
        {
            CollectionAssert.AreEqual(new[]{"SO","CR"},(string[])c.Value("SourceTypes"));CollectionAssert.AreEqual(new[]{"A","B"},(string[])c.Value("CustomerCodes"));
            CollectionAssert.AreEqual(new[]{8,0},(int[])c.Value("LifecycleStatuses"));Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Integer,c.Type("LifecycleStatuses"));
            Assert.AreEqual(NpgsqlDbType.Array|NpgsqlDbType.Text,c.Type("CustomerCodes"));Assert.AreEqual("%a b?~%~_~~';DROP%",c.Value("Keyword"));
            Assert.AreEqual(day.Date,c.Value("RequiredDateFrom"));Assert.AreEqual(DateTime.MaxValue.Date,c.Value("RequiredDateTo"));Assert.AreEqual(NpgsqlDbType.Timestamp,c.Type("RequiredDateTo"));
            foreach(var column in new[]{"g.source_no","so.customer_po_no","so.invoice_no","g.sku_snapshot","g.customer_sku_snapshot","g.link_sku_snapshot"})
                StringAssert.Contains(c.Sql,column+" COLLATE public.erp_dictionary_ci LIKE @Keyword");
            Assert.IsFalse(c.Sql.Contains("c.customer_name COLLATE public.erp_dictionary_ci LIKE",StringComparison.Ordinal));
            Assert.IsFalse(c.Sql.Contains("g.description_snapshot COLLATE public.erp_dictionary_ci LIKE",StringComparison.Ordinal));
            Assert.IsFalse(c.Sql.Contains(request.Keyword,StringComparison.Ordinal));
        }
        Assert.AreEqual(" A ",request.CustomerCodes[0]);Assert.AreEqual(day,request.RequiredDateFrom);
    }

    /// <summary>未传集合默认SO，显式空集合覆盖三类来源，空状态和空白客户不增加筛选。</summary>
    [TestMethod,DataRow(false),DataRow(true)]
    public async Task SourceDefaults_AndExplicitAllRemainDistinct(bool all)
    {
        using var f=new OrderTrackingReadFixture();f.Page(0);
        await f.Sources.SearchSourcesAsync(new(){SourceTypes=all?[]:null!,CustomerCodes=[" ","\t"]},OrderTrackingReadFixture.BusinessDate);
        var c=f.Connection.Commands[0];Assert.AreEqual(!all,c.Parameters.Any(parameter=>parameter.ParameterName=="SourceTypes"));
        if(!all)CollectionAssert.AreEqual(new[]{"SO"},(string[])c.Value("SourceTypes"));
        Assert.IsFalse(c.Parameters.Any(parameter=>parameter.ParameterName=="CustomerCodes"));StringAssert.Contains(c.Sql,"IN ('SO','CR','MD')");
    }

    /// <summary>客户候选不依赖销售权限，仍只取未删除需求引用客户。</summary>
    [TestMethod]
    public async Task CustomerCodes_ReadOnlyDemandCustomers()
    {
        using var f=new OrderTrackingReadFixture(true);
        f.Connection.Results.Enqueue(OrderTrackingReadFixture.Table(new{CustomerCode="A"},new{CustomerCode="B"}));
        CollectionAssert.AreEqual(new[]{"A","B"},await f.Overview.GetCustomerCodesAsync());
        var c=f.Connection.Commands.Single();StringAssert.Contains(c.Sql,"g.customer_id=c.uid AND g.is_deleted=FALSE");
        Assert.AreSame(f.Session.Transaction,c.Transaction);Assert.IsFalse(c.Sql.Contains("sales_order",StringComparison.Ordinal));
    }

    /// <summary>预取消不发全量查询，不结束调用方事务。</summary>
    [TestMethod]
    public async Task Cancellation_StopsBeforeRead()
    {
        using var f=new OrderTrackingReadFixture(true);
        await Assert.ThrowsAsync<OperationCanceledException>(()=>f.Overview.GetSourceDemandsAsync("SO",Guid.NewGuid(),new CancellationToken(true)));
        Assert.HasCount(0,f.Connection.Commands);Assert.AreEqual(0,f.Connection.Commits+f.Connection.Rollbacks);
    }
}

using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using NpgsqlTypes;
using SwiftExport.Contracts.DTOs.OrderTracking.Overview;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Sources;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Materials;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Purchases;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Receipts;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Deliveries;
using SwiftExport.Infrastructure.Repositories.OrderTracking.MaterialDetail;

namespace SwiftExport.Application.Tests.OrderTracking;

/// <summary>真实 PG 只读事务驱动新仓储完整编排，所有业务夹具均为 SELECT CTE，不写任何表。</summary>
[TestClass]
public sealed class OrderTrackingSlicesReadOnlyTests
{
    /// <summary>验证来源双分页、六字段OR、共享采购收货去重、异常证据保留及四区域独立性。</summary>
    [TestMethod]
    public async Task ReadOnlyPg_SourcePagingAndIndependentSlices()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_ORDER_TRACKING_READONLY")!="1") Assert.Inconclusive("未启用跟踪只读PG验证。");
        var root=Root();
        using var config=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"SwiftExport.WebApi","appsettings.json")),new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
        var target=new NpgsqlConnectionStringBuilder(config.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
        Assert.AreEqual("192.168.3.12",target.Host);Assert.AreEqual(5432,target.Port);Assert.AreEqual("dv_next",target.Database);target.IncludeErrorDetail=false;
        await using var connection=new NpgsqlConnection(target.ConnectionString);await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:transaction);
        Assert.AreEqual("on",await connection.ExecuteScalarAsync<string>("SHOW transaction_read_only",transaction:transaction));

        var ids=Enumerable.Range(0,35).Select(_=>Guid.NewGuid()).ToArray();
        var g=ids[0];var g2=ids[1];var g3=ids[2];var bad1=ids[3];var bad2=ids[4];
        var source=ids[5];var sourceB=ids[6];var customer=ids[7];var customerB=ids[8];var product=ids[9];
        var net=ids[10];var net2=ids[11];var net3=ids[12];var po=ids[13];var poLine=ids[14];var supplier=ids[15];
        var receiptHead=ids[16];var reverseHead=ids[17];var receiptLine=ids[18];var reverseLine=ids[19];var warehouse=ids[20];
        var pack=ids[21];var packReverse=ids[22];var draft=ids[23];
        var data=new Dictionary<string,List<Dictionary<string,object?>>>();
        foreach(var table in new[]{"gross_demand","customer","sales_order_header","net_demand","demand_execution_link","purchase_order_head","purchase_order_line","supplier",
            "inbound_document","inbound_document_line","inventory_ledger_entry","packing_list_head","packing_list_line","product","supplier_progress_report","business_operation_log"}) data[table]=[];
        data["gross_demand"].AddRange(new[]{Gross(g,source,customer,"hitSKU","CS-hit","LS-hit"),Gross(g2,source,customerB,"OTHER","",""),Gross(g3,sourceB,customer,"OTHER","",""),
            Gross(bad1,Guid.Empty,customer,"OTHER","",""),Gross(bad2,Guid.Empty,customer,"OTHER","","")});
        data["gross_demand"][1]["lifecycle_status"]=8;
        data["customer"].AddRange(new[]{R(("uid",customer),("customer_code","A"),("customer_name","不属于关键词的客户名"),("is_deleted",false)),
            R(("uid",customerB),("customer_code","B"),("customer_name","客户B"),("is_deleted",false))});
        data["sales_order_header"].Add(R(("uid",source),("is_deleted",false),("customer_po_no","P-O%_?"),("invoice_no","INV-K")));
        data["net_demand"].AddRange(new[]{Net(net,g,6m),Net(net2,g,4m),Net(net3,g3,3m)});
        data["demand_execution_link"].AddRange(new[]{Link(ids[24],net,poLine,po,6m),Link(ids[25],net2,poLine,po,4m),Link(ids[26],net3,poLine,po,3m),
            Link(ids[27],net,ids[28],ids[29],2m)});
        data["purchase_order_head"].Add(R(("uid",po),("order_no","PO-1"),("supplier_id",supplier),("customer_id",customer),("lifecycle_status",0),("confirmation_status",1),("is_deleted",false)));
        data["purchase_order_line"].Add(R(("uid",poLine),("purchase_order_id",po),("line_number",1),("sku","采购快照"),("product_id",product),("unit_snapshot","PCS"),("quantity",20m),("is_deleted",false)));
        data["supplier"].Add(R(("uid",supplier),("supplier_name","实际供应商"),("is_deleted",false)));
        data["inbound_document"].AddRange(new[]{Receipt(receiptHead,1),Receipt(reverseHead,8)});
        data["inbound_document_line"].AddRange(new[]{ReceiptLine(receiptLine,receiptHead,4m),ReceiptLine(reverseLine,reverseHead,3m)});
        var original=ids[30];
        data["inventory_ledger_entry"].AddRange(new[]{
            R(("uid",original),("source_document_id",reverseHead),("source_line_id",reverseLine),("command_item_id",reverseLine),("source_document_type","InboundDocument"),
              ("transaction_type",10),("warehouse_site_id",warehouse),("product_id",product),("quantity_delta",3m),("unit_snapshot","PCS"),("is_deleted",false)),
            R(("uid",ids[31]),("source_document_id",reverseHead),("source_line_id",reverseLine),("source_document_type","InboundDocument"),("transaction_type",900),
              ("warehouse_site_id",warehouse),("product_id",product),("quantity_delta",-3m),("unit_snapshot","PCS"),("is_deleted",false),("reversal_of_ledger_id",original))});
        data["packing_list_head"].AddRange(new[]{Packing(pack,1),Packing(packReverse,8),Packing(draft,0)});
        data["packing_list_line"].AddRange(new[]{PackingLine(ids[32],pack,2m,false),PackingLine(ids[33],packReverse,3m,false),PackingLine(ids[34],draft,1m,true)});
        var commands=new List<NpgsqlCommand>();var commandCount=0;
        using var fixture=new OrderTrackingReadFixture();
        fixture.Connection.ReadAsync=async(record,token)=>
        {
            // 仅替换业务表引用；生产过滤、聚合、Guard和C#编排原样执行。
            var sql=record.Sql;
            var definitions=new List<string>();
            var command=new NpgsqlCommand{Connection=connection,Transaction=transaction};
            var index=0;
            foreach(var (table,rows) in data)
            {
                var name="Fixture"+index++;
                definitions.Add("fixture_"+table+" AS (SELECT * FROM jsonb_populate_recordset(NULL::public."+table+",@"+name+"))");
                sql=sql.Replace("public."+table+" ", "fixture_"+table+" ").Replace("public."+table+"\n","fixture_"+table+"\n");
                sql=sql.Replace("\"public\".\""+table+"\"","fixture_"+table);
                command.Parameters.Add(new NpgsqlParameter(name,NpgsqlDbType.Jsonb){Value=JsonSerializer.Serialize(rows)});
            }
            // QueryMultiple每个语句均需独立CTE作用域；生产SQL内部没有外部拼接或带分号字面量。
            sql=string.Join(";",sql.Split(';',StringSplitOptions.RemoveEmptyEntries).Select(statement=>
                "WITH "+string.Join(",",definitions)+(statement.TrimStart().StartsWith("WITH ",StringComparison.OrdinalIgnoreCase)?","+statement.TrimStart()[5..]:" "+statement)));
            command.CommandText=sql;
            foreach(var parameter in record.Parameters)command.Parameters.Add(parameter.Clone());
            commands.Add(command);commandCount++;
            return await command.ExecuteReaderAsync(token);
        };
        try
        {
            await VerifyMaterialWorkspaceAsync();
            var sources=new PostgreSqlOrderTrackingSourceRepository(fixture.Context);
            // 成品需求本体查询直接执行生产SQL：两模式共享条件，客户按身份匹配，关键词不解释通配符。
            var grossQueries=new SwiftExport.Infrastructure.Repositories.DemandPlanning.Sources.Query.PostgreSqlGrossDemandQueryRepository(fixture.Context);
            var basicPage=await grossQueries.SearchWorkspaceAsync(new(){PageSize=1});Assert.AreEqual(5,basicPage.TotalCount);Assert.HasCount(1,basicPage.Items);
            var chosen=await grossQueries.SearchWorkspaceAsync(new(){SourceUid=source,PageSize=1});Assert.HasCount(2,chosen.Items);
            var exactCustomer=await grossQueries.SearchWorkspaceAsync(new(){SourceUid=source,CustomerIds=[customerB]});CollectionAssert.AreEqual(new[]{g2},exactCustomer.Items.Select(row=>row.Uid).ToArray());
            Assert.HasCount(0,(await grossQueries.SearchWorkspaceAsync(new(){SourceUid=sourceB,CustomerIds=[customerB]})).Items);
            var savedLinkSku=data["gross_demand"][0]["link_sku_snapshot"];data["gross_demand"][0]["link_sku_snapshot"]="literal%_? A";
            foreach(var keyword in new[]{"%","_","? A","%_? A"})
                CollectionAssert.AreEqual(new[]{g},(await grossQueries.SearchWorkspaceAsync(new(){Keyword=keyword})).Items.Select(row=>row.Uid).ToArray(),keyword);
            Assert.HasCount(0,(await grossQueries.SearchWorkspaceAsync(new(){Keyword="?  A"})).Items);
            data["gross_demand"][0]["link_sku_snapshot"]=savedLinkSku;
            Assert.AreEqual(5,(await grossQueries.SearchWorkspaceAsync(new(){RequiredDateFrom=new(2026,10,19,12,0,0),RequiredDateTo=new(2026,10,19)})).TotalCount);
            var options=await grossQueries.GetCustomerOptionsAsync();CollectionAssert.AreEqual(new[]{"A","B"},options.Select(row=>row.Code).ToArray());
            // B只有关闭需求也必须出现在候选；客户删除则不返回，未被非删除需求使用的客户也不返回。
            data["customer"][0]["is_deleted"]=true;CollectionAssert.AreEqual(new[]{customerB},(await grossQueries.GetCustomerOptionsAsync()).Select(row=>row.Id).ToArray());
            data["customer"][0]["is_deleted"]=false;
            var unusedCustomer=R(("uid",Guid.NewGuid()),("customer_code","UNUSED"),("customer_name","无有效需求"),("is_deleted",false));data["customer"].Add(unusedCustomer);
            var deletedDemand=Gross(Guid.NewGuid(),Guid.NewGuid(),(Guid)unusedCustomer["uid"]!,"deleted","","");deletedDemand["is_deleted"]=true;data["gross_demand"].Add(deletedDemand);
            Assert.HasCount(2,await grossQueries.GetCustomerOptionsAsync());data["gross_demand"].Remove(deletedDemand);data["customer"].Remove(unusedCustomer);
            var all=await Groups(new());
            Assert.AreEqual(4,all.TotalCount);Assert.AreEqual(5,all.Items.Sum(item=>item.TotalDemandCount));
            Assert.AreEqual(2,all.Items.Count(item=>item.SourceUid==Guid.Empty));
            Assert.AreEqual(2,all.Items.Where(item=>item.SourceUid==Guid.Empty).Select(item=>item.SourceGroupId).Distinct().Count());
            var hit=await Groups(new(){Keyword="hitSKU"});
            Assert.AreEqual(1,hit.TotalCount);Assert.AreEqual(2,hit.Items.Single().TotalDemandCount);Assert.AreEqual(1,hit.Items.Single().MatchedDemandCount);
            StringAssert.Contains(hit.Items.Single().ReviewReason,"客户不一致");Assert.IsNull(hit.Items.Single().CustomerCode);
            Assert.AreEqual(0,(await Groups(new(){Keyword="不存在"})).TotalCount);
            var first=await Groups(new(){PageSize=1});var second=await Groups(new(){PageSize=1,PageIndex=2});
            Assert.AreNotEqual(first.Items.Single().SourceGroupId,second.Items.Single().SourceGroupId);
            var beyond=await Groups(new(){PageIndex=99,PageSize=1});Assert.AreEqual(4,beyond.TotalCount);Assert.HasCount(0,beyond.Items);
            var child=await fixture.Overview.GetSourceDemandsAsync("SO",source);
            Assert.HasCount(2,child.Items);Assert.IsTrue(child.Items.Any(item=>item.GrossDemandId==g2));
            foreach(var keyword in new[]{"hitSKU","CS-hit","LS-hit","INV-K","P-O%_?","SAME"})
                Assert.IsTrue((await Groups(new(){Keyword=keyword})).TotalCount>0,keyword);
            Assert.AreEqual(0,(await Groups(new(){Keyword="不属于关键词的客户名"})).TotalCount);
            Assert.AreEqual(0,(await Groups(new(){Keyword="不属于关键词的描述"})).TotalCount);
            Assert.AreEqual(0,(await Groups(new(){Keyword="hitSKU",CustomerCodes=["B"]})).TotalCount);
            var materials=new PostgreSqlDemandTrackingMaterialRepository(fixture.Context);
            var purchases=new PostgreSqlDemandTrackingPurchaseRepository(fixture.Context);
            var receipts=new PostgreSqlDemandTrackingReceiptRepository(fixture.Context);
            var deliveries=new PostgreSqlDemandTrackingDeliveryRepository(fixture.Context);
            var materialRows=await materials.GetMaterialsAsync(g);Assert.HasCount(2,materialRows);Assert.AreEqual(6m,materialRows.Single(row=>row.NetDemandId==net).NetQty);
            var purchaseRows=await purchases.GetPurchasesAsync(g);Assert.HasCount(3,purchaseRows);
            Assert.HasCount(3,purchaseRows.Select(row=>row.LinkId).Distinct().ToArray());
            Assert.IsTrue(purchaseRows.Where(row=>row.PurchaseLineId==poLine).All(row=>row.IsSharedPurchaseLine&&row.PurchaseLineQty==20m));
            var missing=purchaseRows.Single(row=>row.PurchaseLineId!=poLine);Assert.IsFalse(missing.HasPurchaseTarget);Assert.IsNull(missing.PurchaseLineQty);Assert.IsFalse(string.IsNullOrEmpty(missing.ReviewReason));
            var received=await receipts.GetReceiptsAsync(g);Assert.HasCount(2,received.Items);
            Assert.AreEqual(2,received.Items.Select(row=>row.InboundLineId).Distinct().Count());
            Assert.AreEqual(true,received.Items.Single(row=>row.InboundLineId==receiptLine).CountsTowardReceipt);
            Assert.AreEqual(false,received.Items.Single(row=>row.InboundLineId==reverseLine).CountsTowardReceipt);
            Assert.IsTrue(received.Items.All(row=>row.IsSharedPurchaseLine));Assert.IsTrue(received.PurchaseLines.Any(line=>line.PurchaseLineQty is null&&!string.IsNullOrEmpty(line.ReviewReason)));
            var delivered=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);Assert.AreEqual(2m,delivered.DeliveredQty);Assert.AreEqual(8m,delivered.RemainingQty);Assert.HasCount(2,delivered.Items);
            // 第5阶段：执行实际批量SQL，对照既有明细，并验证共享去重和不同来源异常隔离。
            var summaryRepo=new SwiftExport.Infrastructure.Repositories.OrderTracking.Summary.PostgreSqlOrderTrackingSummaryRepository(fixture.Context);
            var keys=new SwiftExport.Contracts.DTOs.OrderTracking.Summary.OrderTrackingSourceKeyDto[]{new(){SourceType="SO",SourceGroupId=source},new(){SourceType="SO",SourceGroupId=sourceB}};
            var baseSummary=await summaryRepo.ReadDimensionAsync(keys,"Base",OrderTrackingReadFixture.BusinessDate);
            Assert.HasCount(3,baseSummary);Assert.IsFalse(baseSummary.Single(r=>r.GrossDemandId==g2).IsActive);
            var deliverySummary=await summaryRepo.ReadDimensionAsync(keys,"Delivery",OrderTrackingReadFixture.BusinessDate);
            Assert.AreEqual(delivered.DeliveredQty,deliverySummary.Single(r=>r.GrossDemandId==g).DeliveredQty);
            Assert.AreEqual(delivered.RemainingQty,deliverySummary.Single(r=>r.GrossDemandId==g).RemainingQty);
            var materialSummary=await summaryRepo.ReadDimensionAsync(keys,"Material",OrderTrackingReadFixture.BusinessDate);
            Assert.AreEqual(2,materialSummary.Single(r=>r.GrossDemandId==g).ActiveMaterialCount);
            var purchaseSummary=await summaryRepo.ReadDimensionAsync(keys,"Purchase",OrderTrackingReadFixture.BusinessDate);
            Assert.AreEqual(2,purchaseSummary.Single(r=>r.GrossDemandId==g).ApplicableMaterialCount);
            Assert.AreEqual(1,purchaseSummary.Single(r=>r.GrossDemandId==g).PurchaseReviewMaterialCount);
            var receiptSummary=await summaryRepo.ReadDimensionAsync(keys,"Receipt",OrderTrackingReadFixture.BusinessDate);
            Assert.AreEqual(2,receiptSummary.Single(r=>r.GrossDemandId==g).ReceiptPurchaseLineCount);
            Assert.AreEqual(1,receiptSummary.Single(r=>r.GrossDemandId==g).ReceiptReviewLineCount);
            var sourceReceiptSummary=await summaryRepo.ReadSourceReceiptsAsync(keys,OrderTrackingReadFixture.BusinessDate);
            Assert.AreEqual(2,sourceReceiptSummary.Single(r=>r.SourceGroupId==source).ReceiptPurchaseLineCount);
            Assert.AreEqual(1,sourceReceiptSummary.Single(r=>r.SourceGroupId==sourceB).ReceiptPurchaseLineCount);
            foreach(var code in new[]{"Overdue","MissingRequiredDate","DeliveryReview","NoActiveMaterial","PurchaseShortfall","PurchaseExcess","PurchaseReview","ReceiptOutstanding","ReceiptExcess","ReceiptReview","SourceReview"})
                _=await Groups(new(){AttentionCodes=[code]});
            Assert.AreEqual(0,(await Groups(new(){Keyword="hitSKU",AttentionCodes=["NoActiveMaterial"]})).TotalCount);
            Assert.AreEqual(1,(await Groups(new(){Keyword="hitSKU",AttentionCodes=["NoActiveMaterial","PurchaseReview"]})).TotalCount);
            // 匹配身份复用主表同一行谓词；来源相同不意味着组内其他需求也命中。
            CollectionAssert.AreEquivalent(new[]{g},await Matches(source,new(){Keyword="hitSKU"}));
            Assert.HasCount(0,await Matches(source,new(){Keyword="不存在"}));
            Assert.HasCount(0,await Matches(sourceB,new(){Keyword="hitSKU"}));
            Assert.HasCount(0,await Matches(source,new(){Keyword="hitSKU",CustomerCodes=["B"]}));
            Assert.HasCount(0,await Matches(source,new(){Keyword="hitSKU",AttentionCodes=["NoActiveMaterial"]}));
            CollectionAssert.AreEquivalent(new[]{g},await Matches(source,new(){Keyword="hitSKU",AttentionCodes=["NoActiveMaterial","PurchaseReview"]}));
            foreach(var code in new[]{"Overdue","MissingRequiredDate","DeliveryReview","NoActiveMaterial","PurchaseShortfall","PurchaseExcess","PurchaseReview","ReceiptOutstanding","ReceiptExcess","ReceiptReview","SourceReview"})
            {
                var request=new OrderTrackingSourceSearchRequest{AttentionCodes=[code]};
                var group=(await Groups(request)).Items.SingleOrDefault(row=>row.SourceGroupId==source);
                Assert.AreEqual(group?.MatchedDemandCount??0,(await Matches(source,new(){AttentionCodes=[code]})).Count,code);
            }
            CollectionAssert.AreEquivalent(new[]{bad1},await Matches(bad1,new()));
            // 相同组身份同时存在SO/CR时，默认SO、显式空不限、显式CR不能被默认集合扩为SO+CR。
            var crGross=Gross(Guid.NewGuid(),source,customer,"CR-only","","");crGross["source_type"]="CR";
            data["gross_demand"].Add(crGross);
            Assert.IsTrue((await Groups(new(){SourceTypes=null!})).Items.All(row=>row.SourceType=="SO"));
            Assert.IsTrue((await Groups(new(){SourceTypes=[]})).Items.Any(row=>row.SourceType=="CR"));
            var crGroups=await Groups(new(){SourceTypes=["CR"]});Assert.AreEqual(1,crGroups.TotalCount);Assert.AreEqual("CR",crGroups.Items.Single().SourceType);
            Assert.HasCount(0,await sources.GetSourceMatchesAsync("CR",source,new(){SourceTypes=null!},OrderTrackingReadFixture.BusinessDate));
            CollectionAssert.AreEquivalent(new[]{(Guid)crGross["uid"]!},await sources.GetSourceMatchesAsync("CR",source,new(){SourceTypes=[]},OrderTrackingReadFixture.BusinessDate));
            CollectionAssert.AreEquivalent(new[]{(Guid)crGross["uid"]!},await sources.GetSourceMatchesAsync("CR",source,new(){SourceTypes=["CR"]},OrderTrackingReadFixture.BusinessDate));
            Assert.HasCount(0,await Matches(source,new(){SourceTypes=["CR"]}));
            data["gross_demand"].Remove(crGross);
            var cleanGross=Guid.NewGuid();var cleanSource=Guid.NewGuid();var cleanNet=Guid.NewGuid();var cleanLine=Guid.NewGuid();var cleanPo=Guid.NewGuid();
            data["gross_demand"].Add(Gross(cleanGross,cleanSource,customer,"clean","",""));
            data["net_demand"].Add(Net(cleanNet,cleanGross,5m));
            data["demand_execution_link"].Add(Link(Guid.NewGuid(),cleanNet,cleanLine,cleanPo,5m));
            var cleanPoRow=new Dictionary<string,object?>(data["purchase_order_head"][0]){["uid"]=cleanPo};data["purchase_order_head"].Add(cleanPoRow);
            var cleanLineRow=new Dictionary<string,object?>(data["purchase_order_line"][0]){["uid"]=cleanLine,["purchase_order_id"]=cleanPo};data["purchase_order_line"].Add(cleanLineRow);
            var mixedKeys=keys.Append(new SwiftExport.Contracts.DTOs.OrderTracking.Summary.OrderTrackingSourceKeyDto{SourceType="SO",SourceGroupId=cleanSource}).ToArray();
            data["inbound_document"][1]["reverse_reason"]="";
            var isolated=await summaryRepo.ReadDimensionAsync(mixedKeys,"Receipt",OrderTrackingReadFixture.BusinessDate);
            Assert.AreEqual(0,isolated.Single(r=>r.GrossDemandId==cleanGross).ReceiptReviewLineCount);
            Assert.AreEqual(1,isolated.Single(r=>r.GrossDemandId==cleanGross).ReceiptOutstandingLineCount);
            Assert.IsTrue(isolated.Single(r=>r.GrossDemandId==g).ReceiptReviewLineCount>0);
            data["inbound_document"][1]["reverse_reason"]="纠错";
            data["gross_demand"].RemoveAll(r=>Equals(r["uid"],cleanGross));data["net_demand"].RemoveAll(r=>Equals(r["uid"],cleanNet));
            data["demand_execution_link"].RemoveAll(r=>Equals(r["net_demand_id"],cleanNet));data["purchase_order_head"].Remove(cleanPoRow);data["purchase_order_line"].Remove(cleanLineRow);
            // 正常删除草稿不参与正式净额，也不制造异常；正式删除证据必须保留并变未知。
            data["packing_list_line"][0]["is_deleted"]=true;
            var deletedFormal=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);Assert.IsNull(deletedFormal.DeliveredQty);Assert.HasCount(2,deletedFormal.Items);
            data["packing_list_line"][0]["is_deleted"]=false;
            data["inbound_document"][1]["reverse_reason"]="";
            var unknown=await receipts.GetReceiptsAsync(g);Assert.HasCount(2,unknown.Items);Assert.IsTrue(unknown.Items.All(row=>row.CountsTowardReceipt is null));Assert.IsFalse(string.IsNullOrEmpty(unknown.ReviewReason));
            Assert.AreEqual(2m,(await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate)).DeliveredQty);Assert.HasCount(2,await materials.GetMaterialsAsync(g));Assert.HasCount(3,await purchases.GetPurchasesAsync(g));
            data["inbound_document"][1]["reverse_reason"]="纠错";
            data["purchase_order_line"][0]["product_id"]=Guid.NewGuid();
            Assert.IsTrue((await purchases.GetPurchasesAsync(g)).Where(row=>row.PurchaseLineId==poLine).All(row=>row.ReviewReason.Contains("产品",StringComparison.Ordinal)));
            data["purchase_order_line"][0]["product_id"]=product;
            foreach(var field in new[]{"source_uid","source_line_id","customer_id"})
            {
                var saved=data["gross_demand"][0][field];data["gross_demand"][0][field]=Guid.Empty;
                var invalid=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);Assert.IsNull(invalid.DeliveredQty);Assert.IsNull(invalid.RemainingQty);Assert.IsFalse(string.IsNullOrEmpty(invalid.ReviewReason));
                data["gross_demand"][0][field]=saved;
            }
            // 旧聚合追踪的异常历史保护迁至独立交付区域；未知不能伪装成零交付。
            foreach(var change in new (string Field,object? Value)[]{("demand_basis_fingerprint",null),("demand_basis_fingerprint","A"),
                ("demand_basis_fingerprint",new string('a',64)),("source_no","\t\u3000"),("source_type","so"),("source_version",null),("source_version",0),("unit_snapshot","\t\u3000")})
            {
                var saved=data["gross_demand"][0][change.Field];data["gross_demand"][0][change.Field]=change.Value;
                var invalid=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);
                Assert.IsNull(invalid.DeliveredQty,change.Field);Assert.IsNull(invalid.RemainingQty,change.Field);Assert.IsFalse(string.IsNullOrEmpty(invalid.ReviewReason));
                data["gross_demand"][0][change.Field]=saved;
            }
            var orphan=PackingLine(Guid.NewGuid(),Guid.NewGuid(),1m,false);data["packing_list_line"].Add(orphan);
            var abnormal=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);Assert.IsNull(abnormal.DeliveredQty);Assert.HasCount(3,abnormal.Items);Assert.IsTrue(abnormal.Items.Any(row=>row.ReviewReason.Contains("缺失",StringComparison.Ordinal)));
            data["packing_list_line"].Remove(orphan);
            // 固定业务日验证逾期与选组完全相同；一条需求也只执行两条批量来源查询。
            var savedDate=data["gross_demand"][0]["required_date"];
            var savedLifecycle=data["gross_demand"][0]["lifecycle_status"];
            foreach(var scenario in new[]{("2026-10-18T23:59:59",(bool?)true,1),("2026-10-19T00:00:00",(bool?)false,0),
                ("2026-10-20T00:00:00",(bool?)false,0),((string?)null,(bool?)null,(int?)null)})
            {
                data["gross_demand"][0]["required_date"]=scenario.Item1;
                var state=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);
                Assert.AreEqual(scenario.Item2,state.IsOverdue);Assert.AreEqual(scenario.Item3,state.OverdueDays);
                Assert.AreEqual(OrderTrackingReadFixture.BusinessDate,state.BusinessDate);
                var before=fixture.Connection.Commands.Count;var selected=await Groups(new(){AttentionCodes=["Overdue"]});
                Assert.AreEqual(2,fixture.Connection.Commands.Count-before);
                Assert.AreEqual(state.IsOverdue==true?1:0,selected.TotalCount);
            }
            data["gross_demand"][0]["required_date"]="2026-10-18T00:00:00";
            foreach(var status in new[]{8,9})
            {
                data["gross_demand"][0]["lifecycle_status"]=status;
                var state=await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate);
                Assert.AreEqual(false,state.IsOverdue);Assert.AreEqual(0,state.OverdueDays);
                Assert.AreEqual(0,(await Groups(new(){AttentionCodes=["Overdue"]})).TotalCount);
            }
            data["gross_demand"][0]["lifecycle_status"]=savedLifecycle;
            var savedUnit=data["gross_demand"][0]["unit_snapshot"];
            // 明确覆盖.NET Unicode空白迁入共用SQL的等价边界。
            data["gross_demand"][0]["unit_snapshot"]="\u3000";
            Assert.IsNull((await deliveries.GetDeliveriesAsync(g,OrderTrackingReadFixture.BusinessDate)).IsOverdue);
            Assert.AreEqual(0,(await Groups(new(){AttentionCodes=["Overdue"]})).TotalCount);
            data["gross_demand"][0]["unit_snapshot"]=savedUnit;
            data["gross_demand"][0]["required_date"]=savedDate;
            var materialDetails=new PostgreSqlMaterialTrackingDetailRepository(fixture.Context);
            var materialDetail=await materialDetails.GetMaterialDetailAsync(net);
            Assert.IsNotNull(materialDetail);Assert.AreEqual(g,materialDetail.GrossDemandId);Assert.AreEqual(6m,materialDetail.Material.NetQty);
            // 正常历史与已完全释放的Link保留证据，但不能污染当前可信承接。
            var missingLink=data["demand_execution_link"][3];var savedLinkStatus=missingLink["link_status"];
            missingLink["link_status"]=1;
            data["demand_execution_link"].Add(Link(Guid.NewGuid(),net,Guid.NewGuid(),Guid.NewGuid(),3m));
            var released=data["demand_execution_link"].Last();released["released_qty"]=3m;
            var associated=await purchases.GetMaterialPurchasesAsync(net);
            Assert.IsNotNull(associated);Assert.AreEqual(6m,associated.AssociatedPurchaseQty);Assert.AreEqual(0m,associated.RemainingToPurchaseQty);
            Assert.HasCount(3,associated.Items);Assert.IsTrue(associated.Items.All(item=>item.NetDemandId==net));
            var savedNetQty=data["net_demand"][0]["net_qty"];data["net_demand"][0]["net_qty"]=5m;
            var excess=await purchases.GetMaterialPurchasesAsync(net);
            Assert.AreEqual(-1m,excess!.RemainingToPurchaseQty);StringAssert.Contains(excess.ReviewReason,"超出");
            data["net_demand"][0]["net_qty"]=savedNetQty;
            data["net_demand"][0]["lifecycle_status"]=8;data["net_demand"][0]["is_deleted"]=true;
            var historical=await materialDetails.GetMaterialDetailAsync(net);
            Assert.IsNotNull(historical);Assert.IsTrue(historical.Material.IsDeleted);
            Assert.IsNull((await purchases.GetMaterialPurchasesAsync(net))!.AssociatedPurchaseQty);
            data["net_demand"][0]["lifecycle_status"]=0;data["net_demand"][0]["is_deleted"]=false;
            data["demand_execution_link"].Remove(released);missingLink["link_status"]=savedLinkStatus;

            var perMaterial=await receipts.GetMaterialReceiptsAsync(net);
            Assert.IsNotNull(perMaterial);Assert.AreEqual(net,perMaterial.NetDemandId);Assert.AreEqual(g,perMaterial.GrossDemandId);
            var poProgress=perMaterial.PurchaseLines.Single(line=>line.PurchaseLineId==poLine);
            Assert.AreEqual(4m,poProgress.ReceivedQty);Assert.AreEqual(16m,poProgress.RemainingQty);Assert.IsTrue(poProgress.IsSharedPurchaseLine);
            // 新增正常PO行且无任何入库，必须返回已知0和完整未收，而非缺失进度行。
            var unusedLine=Guid.NewGuid();var unusedLink=Link(Guid.NewGuid(),net,unusedLine,po,1m);
            data["demand_execution_link"].Add(unusedLink);
            data["purchase_order_line"].Add(R(("uid",unusedLine),("purchase_order_id",po),("line_number",2),("sku","无收货"),
                ("product_id",product),("unit_snapshot","PCS"),("quantity",8m),("is_deleted",false)));
            var noReceipt=(await receipts.GetMaterialReceiptsAsync(net))!.PurchaseLines.Single(line=>line.PurchaseLineId==unusedLine);
            Assert.AreEqual(0m,noReceipt.ReceivedQty);Assert.AreEqual(8m,noReceipt.RemainingQty);
            // 未发生的草稿，包含已删除草稿和字段未齐的普通草稿，都不能污染正式已收。
            var draftReceiptHead=Guid.NewGuid();data["inbound_document"].Add(Receipt(draftReceiptHead,0));
            var deletedDraftReceipt=ReceiptLine(Guid.NewGuid(),draftReceiptHead,2m);deletedDraftReceipt["is_deleted"]=true;
            var incompleteDraft=ReceiptLine(Guid.NewGuid(),draftReceiptHead,0m);incompleteDraft["unit_snapshot"]=null;
            data["inbound_document_line"].Add(deletedDraftReceipt);data["inbound_document_line"].Add(incompleteDraft);
            var drafts=await receipts.GetReceiptsAsync(g);
            Assert.IsFalse(drafts.Items.Any(item=>item.InboundLineId==(Guid)deletedDraftReceipt["uid"]!));
            var ordinaryDraft=drafts.Items.Single(item=>item.InboundLineId==(Guid)incompleteDraft["uid"]!);
            Assert.AreEqual(false,ordinaryDraft.CountsTowardReceipt);Assert.AreEqual(string.Empty,ordinaryDraft.ReviewReason);
            Assert.AreEqual(4m,drafts.PurchaseLines.Single(line=>line.PurchaseLineId==poLine).ReceivedQty);
            data["purchase_order_line"][0]["quantity"]=3m;
            var overReceived=(await receipts.GetReceiptsAsync(g)).PurchaseLines.Single(line=>line.PurchaseLineId==poLine);
            Assert.AreEqual(4m,overReceived.ReceivedQty);Assert.AreEqual(-1m,overReceived.RemainingQty);StringAssert.Contains(overReceived.ReviewReason,"超收");
            data["purchase_order_line"][0]["quantity"]=20m;
            data["inbound_document"][1]["reverse_reason"]="";
            Assert.IsNull((await receipts.GetMaterialReceiptsAsync(net))!.PurchaseLines.Single(line=>line.PurchaseLineId==poLine).ReceivedQty);
            Assert.AreEqual(6m,(await purchases.GetMaterialPurchasesAsync(net))!.Items.Single(item=>item.LinkId==ids[24]).LinkedQty);
            data["inbound_document"][1]["reverse_reason"]="纠错";
            Assert.IsNull((await receipts.GetMaterialReceiptsAsync(net))!.PurchaseLines.Single(line=>line.PurchaseLineId==ids[28]).PurchaseLineQty);
            // 302条全量读取：主表命中1条，子表仍包含关闭/作废、空单位/交期及其他未命中行。
            for(var index=0;index<300;index++)
            {
                var extra=Gross(Guid.NewGuid(),source,customer,"不命中","","");
                if(index==0){extra["unit_snapshot"]=null;extra["required_date"]=null;extra["lifecycle_status"]=9;}
                data["gross_demand"].Add(extra);
            }
            var narrow=await Groups(new(){Keyword="hitSKU"});
            Assert.AreEqual(1,narrow.Items.Single().MatchedDemandCount);Assert.AreEqual(302,narrow.Items.Single().TotalDemandCount);
            var complete=await fixture.Overview.GetSourceDemandsAsync("SO",source);
            Assert.HasCount(302,complete.Items);Assert.IsTrue(complete.Items.Any(item=>item.Unit is null&&item.RequiredDate is null));
            var fullWorkspace=await grossQueries.SearchWorkspaceAsync(new(){SourceUid=source,PageSize=1,PageIndex=8});
            Assert.HasCount(302,fullWorkspace.Items);Assert.AreEqual(302,fullWorkspace.TotalCount);Assert.AreEqual(1,fullWorkspace.PageIndex);
            Assert.IsFalse(fixture.Connection.Commands.Last().Sql.Contains("LIMIT",StringComparison.OrdinalIgnoreCase));
            var matchStart=fixture.Connection.Commands.Count;
            var allMatches=await Matches(source,new());
            Assert.HasCount(302,allMatches);Assert.HasCount(302,allMatches.Distinct().ToArray());
            CollectionAssert.AreEquivalent(complete.Items.Select(row=>row.GrossDemandId).ToArray(),allMatches);
            Assert.AreEqual(1,fixture.Connection.Commands.Count-matchStart,"完整命中身份只发出一次集合查询。");
            Assert.IsFalse(fixture.Connection.Commands.Last().Sql.Contains("LIMIT",StringComparison.OrdinalIgnoreCase));
            CollectionAssert.AreEquivalent(new[]{g},await Matches(source,new(){Keyword="hitSKU"}));
            // 同来源存在活动缺交期行，但关键词仍须在该行命中；不能跨行拼接关注条件。
            var noDate=Gross(Guid.NewGuid(),source,customer,"missing-date","","");noDate["required_date"]=null;
            data["gross_demand"].Add(noDate);
            Assert.HasCount(0,await Matches(source,new(){Keyword="hitSKU",AttentionCodes=["MissingRequiredDate"]}));
            CollectionAssert.AreEquivalent(new[]{(Guid)noDate["uid"]!},await Matches(source,new(){AttentionCodes=["MissingRequiredDate"]}));
            noDate["is_deleted"]=true;
            Assert.HasCount(0,await Matches(source,new(){AttentionCodes=["MissingRequiredDate"]}));
            data["gross_demand"].Remove(noDate);
            var allKeys=new SwiftExport.Contracts.DTOs.OrderTracking.Summary.OrderTrackingSourceKeyDto[]{new(){SourceType="SO",SourceGroupId=source}};
            var summaryStart=fixture.Connection.Commands.Count;
            foreach(var dimension in new[]{"Base","Delivery","Material","Purchase","Receipt"})
                Assert.HasCount(302,await summaryRepo.ReadDimensionAsync(allKeys,dimension,OrderTrackingReadFixture.BusinessDate));
            Assert.AreEqual(5,fixture.Connection.Commands.Count-summaryStart,"302条需求仍然仅按维度批量读取。");
            await transaction.RollbackAsync();
            File.WriteAllText(Path.Combine(root,"SwiftExport.Application.Tests","TestResults","订单跟踪分组切片只读PG证据.json"),
                JsonSerializer.Serialize(new{ReadOnly=true,ActualRepositoryCommands=commandCount,SourceGroups=4,FullSourceRows=302,SharedReceiptRows=2,IndependentFailures=true,DeletedDraftExcluded=true,OverdueParity=true,HistoricalMaterialReadable=true,UnpostedReceiptExcluded=true}));
        }
        finally{foreach(var command in commands)await command.DisposeAsync();}

        Task<SwiftExport.Contracts.Wrappers.PagedResponseBase<OrderTrackingSourceGroupDto>> Groups(OrderTrackingSourceSearchRequest request)
            =>new PostgreSqlOrderTrackingSourceRepository(fixture.Context).SearchSourcesAsync(request,OrderTrackingReadFixture.BusinessDate);

        // 本轮物料依据及承接可信度：只替换输入数据，生产管理单SQL、订单跟踪与采购trace均原样执行。
        async Task VerifyMaterialWorkspaceAsync()
        {
            var saved=data.ToDictionary(pair=>pair.Key,pair=>pair.Value.Select(row=>new Dictionary<string,object?>(row)).ToList());
            try
            {
                foreach(var rows in data.Values)rows.Clear();
                var today=TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow,TimeZoneInfo.FindSystemTimeZoneById("China Standard Time")).Date;
                var gross=Gross(g,source,customer,"WORKSPACE","","");data["gross_demand"].Add(gross);
                data["gross_demand"].Add(Gross(g3,source,customer,"OTHER-MATERIAL","",""));
                var material=Net(net,g,10m);material["required_date"]=today.AddDays(-1);data["net_demand"].Add(material);
                data["net_demand"].Add(Net(net2,g3,4m));
                var head=R(("uid",po),("order_no","PO-WS"),("customer_id",customer),("supplier_id",supplier),("origin_kind",1),("confirmation_status",1),("lifecycle_status",0),("is_deleted",false));data["purchase_order_head"].Add(head);
                var line=R(("uid",poLine),("purchase_order_id",po),("line_number",1),("sku","WORKSPACE"),("product_id",product),("quantity",10m),("unlinked_qty",4m),("unit_snapshot","PCS"),("is_deleted",false));data["purchase_order_line"].Add(line);
                var link=Link(ids[24],net,poLine,po,6m);data["demand_execution_link"].Add(link);
                data["customer"].Add(R(("uid",customer),("customer_code","A"),("customer_name","客户"),("is_deleted",false)));
                data["supplier"].Add(R(("uid",supplier),("supplier_name","实际供应商"),("is_deleted",false)));
                data["product"].Add(R(("uid",product),("sku","WORKSPACE"),("unit","PCS"),("material_type","成品"),("supply_type","采购"),("is_deleted",false)));
                var workspace=new SwiftExport.Infrastructure.Repositories.DemandPlanning.Materials.Query.PostgreSqlMaterialDemandQueryRepository(fixture.Context);
                var tracking=new PostgreSqlDemandTrackingPurchaseRepository(fixture.Context);
                async Task<SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand.MaterialDemandManagerItemDto> Read(bool details=true)
                    =>(await workspace.SearchManagerAsync(new(){NetDemandIds=[net]},includePurchaseDetails:details)).Items.Single();
                var before=fixture.Connection.Commands.Count;var normal=await Read();Assert.AreEqual(1,fixture.Connection.Commands.Count-before);
                Assert.AreEqual(6m,normal.AssociatedPurchaseQty);Assert.AreEqual("部分承接",normal.ProcurementStatus);Assert.AreEqual("未承接已逾期",normal.DueHint);
                var hidden=await Read(false);Assert.AreEqual(6m,hidden.AssociatedPurchaseQty);Assert.IsNull(hidden.PurchaseOrderNos);Assert.IsNull(hidden.ActualPurchaseSuppliers);
                // 两类版本快照不再否决相同实物依据；三个真实查询消费者必须一致。
                gross["version"]=8;gross["source_version"]=9;
                Assert.AreEqual(6m,(await Read()).AssociatedPurchaseQty);Assert.AreEqual(6m,(await tracking.GetMaterialPurchasesAsync(net))!.AssociatedPurchaseQty);
                var traceRepo=new SwiftExport.Infrastructure.Repositories.Procurement.PurchaseOrders.Trace.PostgreSqlPurchaseOrderTraceRepository(fixture.Context);
                Assert.IsFalse((await traceRepo.GetAsync(po))!.Lines.Single().Links.Single().NeedsReview);
                foreach(var fingerprint in new string?[]{null,"",new string('B',64),new string('a',64)})
                {
                    gross["demand_basis_fingerprint"]=fingerprint;
                    Assert.IsNull((await Read()).AssociatedPurchaseQty);Assert.IsNull((await tracking.GetMaterialPurchasesAsync(net))!.AssociatedPurchaseQty);
                    Assert.IsTrue((await traceRepo.GetAsync(po))!.Lines.Single().Links.Single().NeedsReview);
                }
                gross["demand_basis_fingerprint"]=new string('A',64);
                // 产品、客户、单位、目标失效和超承接均是需核实，不能退化成未承接。
                foreach(var change in new (Dictionary<string,object?> Row,string Field,object? Value)[]{(line,"product_id",Guid.NewGuid()),(head,"customer_id",Guid.NewGuid()),
                    (line,"unit_snapshot","BOX"),(line,"is_deleted",true),(head,"lifecycle_status",9),(material,"net_qty",5m),
                    (line,"unlinked_qty",3m),(line,"unlinked_qty",null),(line,"unlinked_qty",-1m),(line,"unlinked_qty",11m)})
                {
                    var old=change.Row[change.Field];change.Row[change.Field]=change.Value;var invalid=await Read();
                    Assert.AreEqual("需核实",invalid.ProcurementStatus,change.Field);Assert.IsNull(invalid.AssociatedPurchaseQty,change.Field);change.Row[change.Field]=old;
                }
                // 共用行qty10/unlinked0/links6+4合法；跨物料总量或未关联量不守恒均阻断可信摘要。
                var shared=Link(Guid.NewGuid(),net2,poLine,po,4m);data["demand_execution_link"].Add(shared);line["unlinked_qty"]=0m;
                Assert.AreEqual(6m,(await Read()).AssociatedPurchaseQty);shared["allocated_qty"]=5m;
                Assert.IsNull((await Read()).AssociatedPurchaseQty);data["demand_execution_link"].Remove(shared);line["unlinked_qty"]=4m;
                head["confirmation_status"]=0;Assert.AreEqual(6m,(await Read()).AssociatedPurchaseQty);head["confirmation_status"]=1;
                // 完全释放的历史Link保留在追踪但不占承接；到期提示只说明剩余未承接数量。
                link["released_qty"]=6m;link["link_status"]=1;line["unlinked_qty"]=10m;
                var released=await Read();Assert.AreEqual(0m,released.AssociatedPurchaseQty);Assert.AreEqual("未承接",released.ProcurementStatus);
                Assert.IsTrue((await tracking.GetMaterialPurchasesAsync(net))!.Items.Any(row=>row.LinkId==ids[24]));
                material["required_date"]=today.AddDays(7);Assert.AreEqual("未承接临近交期",(await Read()).DueHint);
                material["required_date"]=today.AddDays(8);Assert.AreEqual("",(await Read()).DueHint);
                for(var i=0;i<300;i++)data["net_demand"].Add(Net(Guid.NewGuid(),g,1m));
                var start=fixture.Connection.Commands.Count;var full=await workspace.SearchManagerAsync(new(){SourceUid=source,PageIndex=99,PageSize=1});
                Assert.HasCount(302,full.Items);Assert.AreEqual(302,full.TotalCount);Assert.AreEqual(1,full.PageIndex);Assert.AreEqual(1,fixture.Connection.Commands.Count-start);
            }
            finally{foreach(var (table,rows) in saved)data[table]=rows;}
        }

        Task<List<Guid>> Matches(Guid sourceId,OrderTrackingSourceMatchRequest request)
            =>new PostgreSqlOrderTrackingSourceRepository(fixture.Context).GetSourceMatchesAsync("SO",sourceId,request,OrderTrackingReadFixture.BusinessDate);

        Dictionary<string,object?> Gross(Guid id,Guid sourceUid,Guid customerId,string sku,string customerSku,string linkSku)=>R(("uid",id),("version",1),("source_type","SO"),("source_uid",sourceUid),("source_line_id",id),
            ("source_no","SAME"),("source_line_number",1),("source_version",1),("demand_basis_fingerprint",new string('A',64)),("customer_id",customerId),("product_id",product),("is_non_standard",false),
            ("sku_snapshot",sku),("customer_sku_snapshot",customerSku),("link_sku_snapshot",linkSku),("description_snapshot","不属于关键词的描述"),("unit_snapshot","PCS"),("demand_qty",10m),
            ("required_date","2026-10-19T00:00:00"),("lifecycle_status",0),("is_deleted",false),("created_date","2026-10-01T00:00:00"));
        Dictionary<string,object?> Net(Guid id,Guid gross,decimal qty)=>R(("uid",id),("version",1),("delivery_mode",0),("gross_demand_id",gross),("root_net_demand_id",id),("decomposition_revision",1),("line_number",1),("lifecycle_status",0),("is_deleted",false),
            ("source_demand_version_snapshot",1),("source_version_snapshot",1),("source_demand_basis_fingerprint_snapshot",new string('A',64)),("sku_snapshot","物料快照"),("unit_snapshot","PCS"),
            ("product_id",product),("gross_qty",qty),("net_qty",qty),("material_type_snapshot","成品"),("supply_type_snapshot","采购"));
        Dictionary<string,object?> Link(Guid id,Guid material,Guid targetLine,Guid targetHead,decimal qty)=>R(("uid",id),("net_demand_id",material),("target_type","PO"),("target_head_id",targetHead),("target_line_id",targetLine),
            ("target_no","PO历史"),("link_status",0),("is_deleted",false),("allocated_qty",qty),("released_qty",0m));
        Dictionary<string,object?> Receipt(Guid id,int status)=>R(("uid",id),("document_no","IN-"+status),("document_type",10),("posting_status",status),("creation_mode",0),("is_deleted",false),
            ("warehouse_site_id",warehouse),("receipt_date","2026-10-19T00:00:00"),("posted_date","2026-10-19T01:00:00"),("reversed_date",status==8?"2026-10-19T02:00:00":null),("reverse_reason",status==8?"纠错":null));
        Dictionary<string,object?> ReceiptLine(Guid id,Guid head,decimal qty)=>R(("uid",id),("inbound_document_id",head),("source_line_id",poLine),("product_id",product),("sku_snapshot","入库快照"),("unit_snapshot","PCS"),("quantity",qty),("is_deleted",false),("line_number",1));
        Dictionary<string,object?> Packing(Guid id,int status)=>R(("uid",id),("packing_list_no","PL-"+status),("posting_status",status),("customer_id",customer),("is_deleted",false),
            ("posted_date",status==0?null:"2026-10-19T00:00:00"),("reversed_date",status==8?"2026-10-19T02:00:00":null));
        Dictionary<string,object?> PackingLine(Guid id,Guid head,decimal qty,bool deleted)=>R(("uid",id),("packing_list_head_id",head),("gross_demand_id",g),("line_number",1),("qty",qty),("unit_snapshot","PCS"),("is_deleted",deleted),
            ("source_type_snapshot","SO"),("source_document_no_snapshot","SAME"),("source_line_number_snapshot",1),("sku_snapshot","hitSKU"),("is_non_standard_snapshot",false),("product_id",product));
    }

    /// <summary>构造仅存在于 JSONB 参数内的物理列事实。</summary>
    private static Dictionary<string,object?> R(params (string Key,object? Value)[] fields)=>fields.ToDictionary(field=>field.Key,field=>field.Value);
    /// <summary>定位当前工作树配置；连接秘密只在内存使用。</summary>
    private static string Root()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SwiftExport.WebApi","appsettings.json")))directory=directory.Parent;
        return directory?.FullName??throw new InvalidOperationException("未找到工作树。");
    }
}

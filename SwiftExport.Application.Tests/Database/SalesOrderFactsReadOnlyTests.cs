using System.Data;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.SalesOrders.Manager;
using SwiftExport.Infrastructure.Data;
using SwiftExport.Infrastructure.Repositories.Base;
using SwiftExport.Infrastructure.Repositories.SalesOrders.Query;
using SwiftExport.Infrastructure.Services.Auth;

namespace SwiftExport.Application.Tests.Database;

/// <summary>显式启用后只读验证真实管理仓储及内联销售头行矩阵，零初始化、零数据库写入。</summary>
public sealed partial class PostgreSqlSalesOrderManagerMigrationTests
{
    /// <summary>在同一只读快照取至多三张订单，并用CTE内联夹具验证空单、单位门槛和整单交期选单。</summary>
    [TestMethod]
    public async Task ReadOnlyFacts_SmokeAndInlineQuantityMatrix()
    {
        if (Environment.GetEnvironmentVariable("SWIFTEXPORT_SALES_FACTS_READONLY") != "1")
            Assert.Inconclusive("本轮只读实库验证未启用。");
        var root = SalesFactsRoot();
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"SwiftExport.WebApi","appsettings.json")),
            new JsonDocumentOptions { CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true });
        var builder = new NpgsqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
        Assert.AreEqual("192.168.3.12",builder.Host);Assert.AreEqual(5432,builder.Port);Assert.AreEqual("dv_next",builder.Database);
        builder.IncludeErrorDetail=false;
        using var session = new DbSession(new PostgreSqlConnectionFactory(builder.ConnectionString));
        await using var transaction = await ((NpgsqlConnection)session.Connection).BeginTransactionAsync(IsolationLevel.RepeatableRead);
        session.Transaction=transaction;
        await session.Connection.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:transaction);
        Assert.AreEqual("on",await session.Connection.ExecuteScalarAsync<string>("SHOW transaction_read_only",transaction:transaction));
        var user=new User();var repository=new PostgreSqlSalesOrderQueryRepository(new(session,NullLoggerFactory.Instance,new AuditStampService(user),user));
        var page=await repository.SearchManagerAsync(new(){PageSize=3});
        Assert.IsTrue(page.Items.Count<=3);
        Assert.AreEqual(await session.Connection.ExecuteScalarAsync<int>("SELECT count(*)::integer FROM public.sales_order_header WHERE is_deleted=FALSE",transaction:transaction),page.TotalCount);
        foreach(var order in page.Items)
        {
            var detail=await repository.GetDetailAsync(order.Uid);Assert.IsNotNull(detail);
            var dates=await session.Connection.QuerySingleAsync<(DateTime? OrderDate,DateTime? DeliveryDate)>(
                "SELECT order_date,delivery_date FROM public.sales_order_header WHERE uid=@Uid",new{order.Uid},transaction);
            Assert.AreEqual(dates.OrderDate,detail.Header.OrderDate);Assert.AreEqual(dates.DeliveryDate,detail.Header.DeliveryDate);
            var lines=(await session.Connection.QueryAsync<(Guid Uid,decimal Qty,string? Image,string? LinkSku)>(
                "SELECT uid,qty,main_image,link_sku FROM public.sales_order_line WHERE sales_order_id=@Uid AND is_deleted=FALSE",new{order.Uid},transaction)).ToDictionary(line=>line.Uid);
            Assert.AreEqual(lines.Count,detail.Lines.Count);
            foreach(var line in detail.Lines)
            {Assert.AreEqual(lines[line.Uid].Qty,line.Qty);Assert.AreEqual(lines[line.Uid].Image,line.MainImage);Assert.AreEqual(lines[line.Uid].LinkSku,line.LinkSku);}
        }
        var ids=Enumerable.Range(0,7).Select(_=>Guid.NewGuid()).ToArray();
        var headers=ids.Select((uid,index)=>new{uid,order_no="FACT-"+index,is_deleted=false,version=1,delivery_date=new DateTime(2026,1,10),total_amount_original_currency=100m}).ToArray();
        var linesFixture=new List<object>();
        void Add(int index,int number,decimal qty,string? unit,DateTime? due=null,bool deleted=false) => linesFixture.Add(new
        {uid=Guid.NewGuid(),sales_order_id=ids[index],line_number=number,sku="S",qty,unit_snapshot=unit,is_deleted=deleted,expected_delivery_date=due});
        Add(1,1,2,"PCS");Add(2,1,3,"PCS",new DateTime(2026,1,1));Add(2,2,4,"pcs ");
        Add(3,1,2,"PCS");Add(3,2,3,"KG");Add(4,1,2," ");
        Add(5,1,2,"PCS");Add(5,2,3,"KG");Add(5,3,1,null);Add(6,1,9,"PCS",deleted:true);
        const string fixtureCtes="""
            WITH sales_order_header AS (SELECT * FROM jsonb_populate_recordset(NULL::public.sales_order_header,@Headers::jsonb)),
            sales_order_line AS (SELECT * FROM jsonb_populate_recordset(NULL::public.sales_order_line,@Lines::jsonb)),
            customer AS (SELECT * FROM jsonb_populate_recordset(NULL::public.customer,'[]'::jsonb))
            """;
        async Task<List<SalesOrderManagerListItemDto>> Inline(SalesOrderManagerSearchRequest request)
        {
            using var capture=new Fixture(false);Queue(capture,Guid.NewGuid(),0,false);await capture.Repository.SearchManagerAsync(request);
            var command=capture.Connection.Commands[1];var sql=command.Sql;
            foreach(var table in new[]{"sales_order_header","sales_order_line","customer"})
                sql=sql.Replace("public."+table,table,StringComparison.Ordinal).Replace("\"public\".\""+table+"\"",table,StringComparison.Ordinal);
            var parameters=new PostgreSqlParameters();
            foreach(var(key,value)in command.Values)
                if(key.StartsWith("EffectiveDue",StringComparison.Ordinal))parameters.AddTimestamp(key,value is DBNull?null:(DateTime?)value);
                else parameters.Add(key,value);
            parameters.Add("Headers",JsonSerializer.Serialize(headers));parameters.Add("Lines",JsonSerializer.Serialize(linesFixture));
            return (await session.Connection.QueryAsync<SalesOrderManagerListItemDto>(fixtureCtes+sql,parameters,transaction)).AsList();
        }
        var matrix=(await Inline(new(){PageSize=20})).ToDictionary(item=>item.Uid);
        Assert.AreEqual(7,matrix.Count);Assert.AreEqual(0m,matrix[ids[0]].TotalQty);Assert.IsNull(matrix[ids[0]].UnitSnapshot);
        Assert.AreEqual(2m,matrix[ids[1]].TotalQty);Assert.AreEqual(7m,matrix[ids[2]].TotalQty);
        Assert.IsNull(matrix[ids[3]].TotalQty);Assert.IsTrue(matrix[ids[3]].HasMixedUnits);Assert.IsFalse(matrix[ids[3]].HasMissingUnit);
        Assert.IsNull(matrix[ids[4]].TotalQty);Assert.IsTrue(matrix[ids[4]].HasMissingUnit);Assert.IsFalse(matrix[ids[4]].HasMixedUnits);
        Assert.IsTrue(matrix[ids[5]].HasMissingUnit&&matrix[ids[5]].HasMixedUnits);Assert.AreEqual(0m,matrix[ids[6]].TotalQty);
        var selected=(await Inline(new(){EffectiveDeliveryDateFrom=new(2026,1,1),EffectiveDeliveryDateTo=new(2026,1,1)})).Single();
        Assert.AreEqual(ids[2],selected.Uid);Assert.AreEqual(7m,selected.TotalQty);Assert.AreEqual(100m,selected.TotalAmountOriginalCurrency);
        await transaction.RollbackAsync();session.Transaction=null;
        File.WriteAllText(Path.Combine(root,"SwiftExport.Application.Tests","TestResults","销售事实只读冒烟证据.json"),
            JsonSerializer.Serialize(new{ReadOnly=true,LiveTotal=page.TotalCount,LiveDetails=page.Items.Count,InlineCases=7,WholeOrderFilter=true}));
    }

    /// <summary>定位当前工作树，不读取其他聊天的配置。</summary>
    private static string SalesFactsRoot()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SwiftExport-WebAPI.slnx")))directory=directory.Parent;
        return directory?.FullName??throw new DirectoryNotFoundException();
    }
}

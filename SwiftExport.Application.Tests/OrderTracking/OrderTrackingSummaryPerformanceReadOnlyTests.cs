using System.Data;
using System.Diagnostics;
using System.Text.Json;
using Dapper;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Contracts.DTOs.OrderTracking.Sources;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Sources;
using SwiftExport.Infrastructure.Repositories.OrderTracking.Summary;
using SwiftExport.Application.Tests.OrderTracking.Baseline;
namespace SwiftExport.Application.Tests.OrderTracking;
/// <summary>真实业务库只读性能对照；不使用CTE夹具、清缓存、索引或任何写入。</summary>
[TestClass]
public sealed class OrderTrackingSummaryPerformanceReadOnlyTests
{
    /// <summary>交替采样本轮前后相同分页条件，并单列新增摘要成本及SQL次数。</summary>
    [TestMethod]
    public async Task ReadOnlyPg_CompareBaselineAndSummaryCost()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_ORDER_TRACKING_READONLY")!="1")Assert.Inconclusive("未启用只读性能验证。");
        var directory=new DirectoryInfo(AppContext.BaseDirectory);
        while(directory is not null&&!File.Exists(Path.Combine(directory.FullName,"SwiftExport.WebApi","appsettings.json")))directory=directory.Parent;
        var root=directory!.FullName;
        using var json=JsonDocument.Parse(File.ReadAllText(Path.Combine(root,"SwiftExport.WebApi","appsettings.json")),new JsonDocumentOptions{CommentHandling=JsonCommentHandling.Skip,AllowTrailingCommas=true});
        var target=new NpgsqlConnectionStringBuilder(json.RootElement.GetProperty("ConnectionStrings").GetProperty("Default").GetString());
        Assert.AreEqual("192.168.3.12",target.Host);Assert.AreEqual("dv_next",target.Database);Assert.AreEqual(5432,target.Port);
        target.IncludeErrorDetail=false;
        await using var connection=new NpgsqlConnection(target.ConnectionString);await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        await connection.ExecuteAsync("SET TRANSACTION READ ONLY",transaction:transaction);
        Assert.AreEqual("on",await connection.ExecuteScalarAsync<string>("SHOW transaction_read_only",transaction:transaction));
        using var fixture=new OrderTrackingReadFixture();var commands=new List<NpgsqlCommand>();
        fixture.Connection.ReadAsync=async(record,token)=>{
            var cmd=new NpgsqlCommand(record.Sql,connection,transaction);
            foreach(var parameter in record.Parameters)cmd.Parameters.Add(parameter.Clone());
            commands.Add(cmd);return await cmd.ExecuteReaderAsync(token);
        };
        try{
            var baseline=new BaselinePostgreSqlOrderTrackingSourceRepository(fixture.Context);
            var current=new PostgreSqlOrderTrackingSourceRepository(fixture.Context);
            var date=new DateTime(2026,10,19);
            var initial=await current.SearchSourcesAsync(new(){PageSize=20},date);
            var keyword=initial.Items.FirstOrDefault()?.SourceNo??"不存在的性能样本";
            var scenarios=new[]{("Default",new OrderTrackingSourceSearchRequest{PageSize=20}),
                ("Keyword",new OrderTrackingSourceSearchRequest{PageSize=20,Keyword=keyword}),
                ("Overdue",new OrderTrackingSourceSearchRequest{PageSize=20,AttentionCodes=["Overdue"]})};
            var results=new List<object>();
            foreach(var (name,request) in scenarios){
                var old=await baseline.SearchSourcesAsync(request,date);var now=await current.SearchSourcesAsync(request,date);
                Assert.AreEqual(old.TotalCount,now.TotalCount,name);
                CollectionAssert.AreEqual(old.Items.Select(x=>x.SourceGroupId).ToArray(),now.Items.Select(x=>x.SourceGroupId).ToArray(),name);
                // 两轮暖机后交替运行七次，使用同一只读快照、连接、参数和数据量。
                for(var i=0;i<2;i++){await baseline.SearchSourcesAsync(request,date);await current.SearchSourcesAsync(request,date);}
                var before=new List<double>();var after=new List<double>();var start=commands.Count;
                for(var i=0;i<7;i++){
                    var watch=Stopwatch.StartNew();await baseline.SearchSourcesAsync(request,date);before.Add(watch.Elapsed.TotalMilliseconds);
                    watch.Restart();await current.SearchSourcesAsync(request,date);after.Add(watch.Elapsed.TotalMilliseconds);
                }
                Assert.AreEqual(28,commands.Count-start);
                results.Add(new{Name=name,TotalSources=now.TotalCount,PageRows=now.Items.Count,BaselineMedianMs=Median(before),CurrentMedianMs=Median(after),BaselineSamplesMs=before,CurrentSamplesMs=after,SqlPerCall=2});
            }
            var keys=initial.Items.Select(x=>new OrderTrackingSourceKeyDto{SourceType=x.SourceType,SourceGroupId=x.SourceGroupId}).ToList();
            var repo=new PostgreSqlOrderTrackingSummaryRepository(fixture.Context);
            var service=OrderTrackingSummaryServiceTests.Service(fixture,repo,OrderTrackingSummaryServiceTests.All());
            var summaryTimes=new List<double>();var demandTimes=new List<double>();var sourceSql=0;var demandSql=0;var demandCount=0;
            for(var i=0;i<9;i++){
                var start=commands.Count;var watch=Stopwatch.StartNew();var result=await service.GetSourceSummariesAsync(new(){Sources=keys});
                Assert.IsTrue(result.Success,result.Message);
                Assert.IsTrue(result.Data.Items.All(x=>x.DeliveryAvailability=="Readable"&&x.MaterialAvailability=="Readable"&&x.PurchaseAvailability=="Readable"&&x.ReceiptAvailability=="Readable"));
                if(i>=2){summaryTimes.Add(watch.Elapsed.TotalMilliseconds);sourceSql=commands.Count-start;}
                if(keys.Count>0){
                    start=commands.Count;watch.Restart();var demands=await service.GetSourceDemandSummariesAsync(keys[0].SourceType,keys[0].SourceGroupId);
                    Assert.IsTrue(demands.Success,demands.Message);demandCount=demands.Data.Items.Count;
                    Assert.IsTrue(demands.Data.Items.All(x=>x.DeliveryAvailability=="Readable"&&x.MaterialAvailability=="Readable"&&x.PurchaseAvailability=="Readable"&&x.ReceiptAvailability=="Readable"));
                    if(i>=2){demandTimes.Add(watch.Elapsed.TotalMilliseconds);demandSql=commands.Count-start;}
                }
            }
            // 保存实际执行计划，避免仅凭秒表判断索引收益；EXPLAIN ANALYZE只执行本轮SELECT。
            var planCommand=commands.LastOrDefault(c=>c.CommandText.Contains("summary_receipt_counts",StringComparison.Ordinal));
            if(planCommand is not null){
                await using var plan=new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) "+planCommand.CommandText,connection,transaction);
                foreach(NpgsqlParameter parameter in planCommand.Parameters)plan.Parameters.Add(parameter.Clone());
                var planJson=await plan.ExecuteScalarAsync();
                File.WriteAllText(Path.Combine(root,"SwiftExport.Application.Tests","TestResults","stage5-summary-plan.json"),Convert.ToString(planJson));
            }
            var report=new{Database="192.168.3.12:5432/dv_next",ReadOnly=true,Fixture=false,BusinessDate=date,Warmup=2,Samples=7,Scenarios=results,
                SourceSummary=new{Sources=keys.Count,MedianMs=Median(summaryTimes),SqlPerCall=sourceSql,SamplesMs=summaryTimes},
                DemandSummary=new{Demands=demandCount,MedianMs=Median(demandTimes),SqlPerCall=demandSql,SamplesMs=demandTimes},
                Limitations="仓储/服务读查询耗时，不含网络HTTP、图片和WPF渲染；新增摘要没有旧版同功能接口，不把基础分页和新增功能作等价加速比较。"};
            File.WriteAllText(Path.Combine(root,"SwiftExport.Application.Tests","TestResults","stage5-performance.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
            await transaction.RollbackAsync();
        }finally{foreach(var command in commands)await command.DisposeAsync();}
    }
    private static double Median(List<double> samples)=>samples.Count==0?0:samples.Order().ElementAt(samples.Count/2);
}

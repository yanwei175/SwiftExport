using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.DemandPlanning;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Query;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Core.Interfaces.OrderTracking.Purchases;
using SwiftExport.Core.Interfaces.OrderTracking.Receipts;
using SwiftExport.Core.Interfaces.OrderTracking.Summary;
using SwiftExport.Contracts.DTOs.OrderTracking.Summary;
using SwiftExport.Application.Interfaces.DemandPlanning.Materials.Reversal;
using SwiftExport.Core.Interfaces.OrderTracking.Sources;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Text.Json;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Auditing;
using SwiftExport.Infrastructure;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>仅在本轮获批新专库验证采购草稿的占用、回执及真实PG竞争；从不清理或重置已有事实。</summary>
[TestClass,DoNotParallelize,TestCategory("PostgreSqlIntegration")]
public sealed class PurchaseDraftLifecyclePostgreSqlTests
{
    private const string Database="dv_purchase_draft_lifecycle_tests_20261020";
    private static readonly Guid Actor=new("20261020-0000-0000-0000-000000000002");
    private static readonly Guid DeniedActor=new("20261020-0000-0000-0000-000000000003");
    private static readonly Guid Customer=new("20261020-0000-0000-0000-000000000010");
    private static readonly Guid Supplier=new("20261020-0000-0000-0000-000000000011");
    private static readonly Guid Product=new("20261020-0000-0000-0000-000000000020");

    /// <summary>复现单源多供应商及客供父子创建；原输入落入本地证据后原样重试，证明修复不会重建已提交结果。</summary>
    [TestMethod]
    public async Task RealPg_MultiSupplierSourceAuditAndRetry()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PURCHASE_DRAFT_PG")!="1")Assert.Inconclusive("未启用本轮专库门禁。");
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(Root(),"SwiftExport.WebApi/appsettings.json")).Build();
        var settings=new NpgsqlConnectionStringBuilder(config.GetConnectionString("Default"));
        Assert.AreEqual("192.168.3.12",settings.Host);Assert.AreEqual(5432,settings.Port);settings.Database=Database;settings.Pooling=false;
        await using var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();Assert.AreEqual(Database,connection.Database);
        var path=Path.Combine(Root(),"SwiftExport.Application.Tests/TestResults/purchase-draft-multisupplier-input.json");
        MultiSupplierInput input;
        if(File.Exists(path))input=JsonSerializer.Deserialize<MultiSupplierInput>(await File.ReadAllTextAsync(path))!;
        else
        {
            input=new(){Source=Guid.NewGuid(),GrossA=Guid.NewGuid(),GrossB=Guid.NewGuid(),Parent=Guid.NewGuid(),Other=Guid.NewGuid(),Child=Guid.NewGuid(),
                ChildProduct=Guid.NewGuid(),ChildSupplier=Guid.NewGuid(),AppendHead=Guid.NewGuid(),AppendLine=Guid.NewGuid()};
            await connection.ExecuteAsync("""
                INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active)
                    VALUES(@ChildSupplier,@SupplierCode,'多供应商客供验收','AUD',TRUE);
                INSERT INTO public.product(uid,sku,unit,material_type,supply_type,bom_version,color_id,is_active)
                    VALUES(@ChildProduct,@Sku,'PCS','客供件','采购',0,'00000000-0000-0000-0000-000000000000',TRUE);
                INSERT INTO public.gross_demand(uid,source_type,source_uid,source_line_id,source_line_number,source_no,customer_id,product_id,
                    sku_snapshot,unit_snapshot,demand_qty,demand_basis_fingerprint,lifecycle_status,is_non_standard) VALUES
                    (@GrossA,'MD',@Source,@GrossA,1,@SourceNo,@Customer,@Product,'PDT-FG','PCS',10,@Fingerprint,0,FALSE),
                    (@GrossB,'MD',@Source,@GrossB,2,@SourceNo,@Customer,@Product,'PDT-FG','PCS',10,@Fingerprint,0,FALSE);
                INSERT INTO public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,decomposition_revision,
                    line_number,root_net_demand_id,parent_net_demand_id,delivery_target_net_demand_id,product_id,sku_snapshot,material_type_snapshot,supply_type_snapshot,
                    unit_snapshot,gross_qty,net_qty,lifecycle_status,delivery_mode) VALUES
                    (@Parent,@GrossA,1,@Fingerprint,1,1,@Parent,NULL,NULL,@Product,'PDT-FG','成品','采购','PCS',10,10,0,0),
                    (@Other,@GrossB,1,@Fingerprint,1,1,@Other,NULL,NULL,@Product,'PDT-FG','成品','采购','PCS',10,10,0,0),
                    (@Child,@GrossA,1,@Fingerprint,1,2,@Parent,@Parent,@Parent,@ChildProduct,@Sku,'客供件','采购','PCS',5,5,0,1);
                INSERT INTO public.purchase_order_head(uid,origin_kind,confirmation_status,lifecycle_status,order_no,currency,customer_id,customer_code,
                    customer_name_snapshot,supplier_id,supplier_code,supplier_name,demand_source_type,demand_source_uid,demand_source_no,execution_request_id,order_date)
                    VALUES(@AppendHead,1,0,0,@AppendNo,'',@Customer,'PDT-C','验收客户',@Supplier,'PDT-S','验收供应商','MD',@Source,@SourceNo,gen_random_uuid(),LOCALTIMESTAMP);
                INSERT INTO public.purchase_order_line(uid,purchase_order_id,line_number,order_no,product_id,sku,quantity,unlinked_qty,unit_snapshot,
                    material_type,supply_type,unit_price,tax_price,unit_amount,tax_amount,source_type,source_uid,source_no)
                    VALUES(@AppendLine,@AppendHead,1,@AppendNo,@Product,'PDT-FG',1,0,'PCS','成品','采购',2,2.5,2,2.5,'MD',@Source,@SourceNo);
                INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,target_head_id,target_line_id,target_no,
                    link_status,allocated_qty,released_qty) VALUES(gen_random_uuid(),@Parent,gen_random_uuid(),gen_random_uuid(),'PO',@AppendHead,@AppendLine,@AppendNo,0,1,0);
                """,new{input.Source,input.GrossA,input.GrossB,input.Parent,input.Other,input.Child,input.ChildProduct,input.ChildSupplier,input.AppendHead,input.AppendLine,
                    SupplierCode="AUD-"+input.ChildSupplier.ToString("N"),Sku="AUD-"+input.ChildProduct.ToString("N"),SourceNo="AUD-"+input.Source.ToString("N"),
                    AppendNo="AUD-APP-"+input.AppendHead.ToString("N"),Customer,Supplier,Product,Fingerprint=new string('A',64)});
            input.Command=new(){ExecutionRequestId=Guid.NewGuid(),EntryMode=PurchaseDraftEntryModeEnum.ManualSelection,Sources=[new(){SourceType="MD",SourceUid=input.Source,
                Items=[new(){CommandItemId=Guid.NewGuid(),NetDemandId=input.Parent,ExpectedVersion=1,PurchaseQty=4,SupplierId=Supplier,UnitPrice=2,TaxPrice=2.5m},
                    new(){CommandItemId=Guid.NewGuid(),NetDemandId=input.Other,ExpectedVersion=1,PurchaseQty=3,SupplierId=new Guid("20261020-0000-0000-0000-000000000012"),UnitPrice=2,TaxPrice=2.5m},
                    new(){CommandItemId=Guid.NewGuid(),NetDemandId=input.Child,ExpectedVersion=1,PurchaseQty=2,SupplierId=input.ChildSupplier,DeliveryTargetSupplierId=Supplier,UnitPrice=1,TaxPrice=1}],
                Targets=[new(){SupplierId=Supplier,TargetMode=PurchaseDraftTargetModeEnum.AppendExisting,ExistingPurchaseOrderId=input.AppendHead,ExpectedPurchaseOrderVersion=1}]}]};
            await File.WriteAllTextAsync(path,JsonSerializer.Serialize(input));
        }
        var exceptions=new ConcurrentQueue<Exception>();var services=new ServiceCollection();services.AddLogging(builder=>builder.AddProvider(new ExceptionCapture(exceptions)));
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Default"]=settings.ConnectionString}).Build());
        services.AddSingleton<ICurrentUser>(new TestUser{UserId=Actor});services.AddSingleton<ICorrelationContext>(new Correlation());services.AddInfrastructureServices();services.AddApplicationServices();
        await using var provider=services.BuildServiceProvider();
        async Task<T> Run<T>(Func<IServiceProvider,Task<T>> action){await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider);}
        var executed=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(input.Command));Assert.IsTrue(executed.Success,executed.Message);
        var result=executed.Data.Sources.Single();var commandId=SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand.PurchaseDraftCreationService.SourceCommandId(input.Command.ExecutionRequestId,"MD",input.Source);
        var heads=await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE execution_request_id=@Id",new{Id=input.Command.ExecutionRequestId});
        var links=await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE execution_request_id=@Id",new{Id=input.Command.ExecutionRequestId});
        var receipts=await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id",new{Id=commandId});
        var audit=await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id",new{Id=commandId});
        var postgres=exceptions.OfType<PostgresException>().FirstOrDefault();
        await File.WriteAllTextAsync(Path.Combine(Path.GetDirectoryName(path)!,"purchase-draft-multisupplier-evidence.json"),JsonSerializer.Serialize(new{
            Host=settings.Host,Port=settings.Port,Database,input.Source,input.Command.ExecutionRequestId,SourceCommandId=commandId,result.Outcome,result.ErrorCode,
            SqlState=postgres?.SqlState,Constraint=postgres?.ConstraintName,NewHeads=heads,NewLinks=links,Receipts=receipts,Audit=audit}));
        if(result.Outcome==NetDemandCommandOutcomeEnum.Failed)
        {
            Assert.AreEqual(0,heads);Assert.AreEqual(0,links);Assert.AreEqual(0,receipts);Assert.AreEqual(0,audit);
            Assert.AreEqual(1,await connection.ExecuteScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@Id",new{Id=input.AppendHead}));
        }
        Assert.AreNotEqual(NetDemandCommandOutcomeEnum.Failed,result.Outcome,$"{result.ErrorCode}: {result.Message}; {postgres?.SqlState}/{postgres?.ConstraintName}");
        Assert.AreEqual(2,heads);Assert.AreEqual(3,links);Assert.AreEqual(1,receipts);Assert.AreEqual(1,audit);Assert.AreEqual(3,result.PurchaseDrafts.Count);
        Assert.AreEqual(1,result.PurchaseDrafts.Count(draft=>draft.AppendedToExisting));
        foreach(var draft in result.PurchaseDrafts)
        {
            var trace=await Run(sp=>sp.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(draft.PurchaseOrderId));Assert.IsTrue(trace.Success,trace.Message);
            Assert.AreEqual(1,trace.Data.Operations.Count(operation=>operation.OperationType=="CreateDraftsFromDemand"
                && operation.Summary?.Contains(result.PurchaseDrafts.First(row=>!row.AppendedToExisting).OrderNo)==true));
        }
        var unrelated=await connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.purchase_order_head WHERE uid<>ALL(@Ids) AND NOT is_deleted LIMIT 1",new{Ids=result.PurchaseDrafts.Select(draft=>draft.PurchaseOrderId).ToArray()});
        var unrelatedTrace=await Run(sp=>sp.GetRequiredService<IPurchaseOrderTraceService>().GetAsync(unrelated));Assert.IsTrue(unrelatedTrace.Success,unrelatedTrace.Message);
        Assert.AreEqual(0,unrelatedTrace.Data.Operations.Count(operation=>operation.OperationType=="CreateDraftsFromDemand" && operation.Summary?.Contains(result.PurchaseDrafts[0].OrderNo)==true));
        var replay=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(input.Command));Assert.IsTrue(replay.Success,replay.Message);
        Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied,replay.Data.Sources.Single().Outcome);Assert.AreEqual(3,replay.Data.Sources.Single().PurchaseDrafts.Count);
        Assert.AreEqual(1,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id",new{Id=commandId}));
    }

    /// <summary>同专库真实采购写入后注入审计故障，验证确定回滚零残留且相同原命令恢复；不建触发器或改动数据库结构。</summary>
    [TestMethod]
    public async Task RealPg_MultiSupplierAuditFailureRollsBackAndRetries()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PURCHASE_DRAFT_PG")!="1")Assert.Inconclusive("未启用本轮专库门禁。");
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(Root(),"SwiftExport.WebApi/appsettings.json")).Build();
        var settings=new NpgsqlConnectionStringBuilder(config.GetConnectionString("Default"));Assert.AreEqual("192.168.3.12",settings.Host);Assert.AreEqual(5432,settings.Port);
        settings.Database=Database;settings.Pooling=false;await using var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();Assert.AreEqual(Database,connection.Database);
        var input=JsonSerializer.Deserialize<MultiSupplierInput>(await File.ReadAllTextAsync(Path.Combine(Root(),"SwiftExport.Application.Tests/TestResults/purchase-draft-multisupplier-input.json")))!;
        input.Command.ExecutionRequestId=Guid.NewGuid();foreach(var item in input.Command.Sources[0].Items)item.CommandItemId=Guid.NewGuid();
        var version=await connection.ExecuteScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@Id",new{Id=input.AppendHead});
        input.Command.Sources[0].Targets[0].ExpectedPurchaseOrderVersion=version;
        var fault=new AuditFault{Reject=true};var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Default"]=settings.ConnectionString}).Build());
        services.AddSingleton<ICurrentUser>(new TestUser{UserId=Actor});services.AddSingleton<ICorrelationContext>(new Correlation());services.AddInfrastructureServices();services.AddApplicationServices();
        services.AddScoped<IBusinessOperationLogRepository>(sp=>new FailingAudit(new PostgreSqlBusinessOperationLogRepository(sp.GetRequiredService<RepositoryBaseContext>()),fault));
        await using var provider=services.BuildServiceProvider();
        async Task<SwiftExport.Contracts.Wrappers.ServiceResult<CreatePurchaseDraftsResult>> Create()
        {await using var scope=provider.CreateAsyncScope();return await scope.ServiceProvider.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(input.Command);}
        var failed=await Create();Assert.IsTrue(failed.Success);var failure=failed.Data.Sources.Single();Assert.AreEqual("WRITE_FAILED",failure.ErrorCode);Assert.IsEmpty(failure.PurchaseDrafts);
        var commandId=SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand.PurchaseDraftCreationService.SourceCommandId(input.Command.ExecutionRequestId,"MD",input.Source);
        Assert.AreEqual(0,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE execution_request_id=@Id",new{Id=input.Command.ExecutionRequestId}));
        Assert.AreEqual(0,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE execution_request_id=@Id",new{Id=input.Command.ExecutionRequestId}));
        Assert.AreEqual(0,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id",new{Id=commandId}));
        Assert.AreEqual(0,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id",new{Id=commandId}));
        Assert.AreEqual(version,await connection.ExecuteScalarAsync<int>("SELECT version FROM public.purchase_order_head WHERE uid=@Id",new{Id=input.AppendHead}));
        fault.Reject=false;var recovered=await Create();Assert.IsTrue(recovered.Success);Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded,recovered.Data.Sources.Single().Outcome,recovered.Message);
        var replay=await Create();Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied,replay.Data.Sources.Single().Outcome);
        Assert.AreEqual(2,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE execution_request_id=@Id",new{Id=input.Command.ExecutionRequestId}));
        Assert.AreEqual(3,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.demand_execution_link WHERE execution_request_id=@Id",new{Id=input.Command.ExecutionRequestId}));
        Assert.AreEqual(1,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.command_receipt WHERE command_id=@Id",new{Id=commandId}));
        Assert.AreEqual(1,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.business_operation_log WHERE command_id=@Id",new{Id=commandId}));
        await File.WriteAllTextAsync(Path.Combine(Root(),"SwiftExport.Application.Tests/TestResults/purchase-draft-audit-rollback-evidence.json"),JsonSerializer.Serialize(new{
            Host=settings.Host,Port=settings.Port,Database,input.Command.ExecutionRequestId,SourceCommandId=commandId,ErrorCode=failure.ErrorCode,
            FailureHadNoHeadsLinksReceiptsAudit=true,ExistingAppendUnchanged=true,SameCommandRecovered=true,ReplayDidNotDuplicate=true}));
    }

    /// <summary>仅控制当前专库测试请求的审计端口，不修改真实仓储或约束。</summary>
    private sealed class AuditFault{public bool Reject{get;set;}}
    /// <summary>真实写事务照常运行，仅在审计插入前注入确定异常，所有其他审计能力原样委托。</summary>
    private sealed class FailingAudit(IBusinessOperationLogRepository inner,AuditFault fault):IBusinessOperationLogRepository
    {
        public Task<BusinessOperationLog?> GetByModuleCommandAsync(string module,Guid commandId)=>inner.GetByModuleCommandAsync(module,commandId);
        public Task<BusinessOperationLog?> GetByCommandAsync(string module,string operationType,Guid commandId)=>inner.GetByCommandAsync(module,operationType,commandId);
        public Task<int> InsertAsync(BusinessOperationLog log)=>fault.Reject?throw new IOException("专库定向注入审计故障"):inner.InsertAsync(log);
        public Task<IReadOnlyList<BusinessOperationTraceItemDto>> GetByAggregateAsync(string module,Guid aggregateUid,CancellationToken cancellationToken=default)
            =>inner.GetByAggregateAsync(module,aggregateUid,cancellationToken);
    }

    /// <summary>仅保存专库生成的固定输入，以相同ExecutionRequestId验证修复前失败后的恢复；不读取用户业务请求。</summary>
    private sealed class MultiSupplierInput
    {
        public Guid Source{get;set;}public Guid GrossA{get;set;}public Guid GrossB{get;set;}public Guid Parent{get;set;}public Guid Other{get;set;}public Guid Child{get;set;}
        public Guid ChildProduct{get;set;}public Guid ChildSupplier{get;set;}public Guid AppendHead{get;set;}public Guid AppendLine{get;set;}public CreatePurchaseDraftsCommand Command{get;set;}=new();
    }
    /// <summary>仅捕获异常类型及SQL状态供专库断言，不打印请求、密码或原始业务输入。</summary>
    private sealed class ExceptionCapture(ConcurrentQueue<Exception> errors):ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)=>new CaptureLogger(errors);public void Dispose(){}
        private sealed class CaptureLogger(ConcurrentQueue<Exception> errors):ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)where TState:notnull=>null;public bool IsEnabled(LogLevel level)=>true;
            public void Log<TState>(LogLevel level,EventId eventId,TState state,Exception? exception,Func<TState,Exception?,string> formatter){if(exception is not null)errors.Enqueue(exception);}
        }
    }

    /// <summary>真实查询补关联候选、采购管理和跟踪摘要；草稿关联不计已采购，仍阻止成品需求回退。</summary>
    [TestMethod]
    public async Task RealPg_ConsumersAssociationAndRollbackGuard()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PURCHASE_DRAFT_PG")!="1")Assert.Inconclusive("未启用本轮专库门禁。");
        var config=new ConfigurationBuilder().AddJsonFile(Path.Combine(Root(),"SwiftExport.WebApi/appsettings.json")).Build();
        var settings=new NpgsqlConnectionStringBuilder(config.GetConnectionString("Default"));
        Assert.AreEqual("192.168.3.12",settings.Host);Assert.AreEqual(5432,settings.Port);settings.Database=Database;settings.Pooling=false;
        await using var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();Assert.AreEqual(Database,connection.Database);
        var source=Guid.NewGuid();var demand=Guid.NewGuid();
        await connection.ExecuteAsync("""
            INSERT INTO public.gross_demand(uid,source_type,source_uid,source_line_id,source_no,customer_id,product_id,sku_snapshot,unit_snapshot,
                demand_qty,demand_basis_fingerprint,lifecycle_status,is_non_standard)
            VALUES(@Source,'MD',@Source,@Source,@No,@Customer,@Product,'PDT-FG','PCS',10,@Fingerprint,0,FALSE);
            INSERT INTO public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,decomposition_revision,
                line_number,root_net_demand_id,product_id,sku_snapshot,material_type_snapshot,supply_type_snapshot,unit_snapshot,gross_qty,net_qty,lifecycle_status,delivery_mode)
            VALUES(@Demand,@Source,1,@Fingerprint,1,1,@Demand,@Product,'PDT-FG','成品','采购','PCS',10,10,0,0);
            """,new{Source=source,Demand=demand,No="PDT-Q-"+source.ToString("N"),Customer,Product,Fingerprint=new string('A',64)});
        var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string,string?>{["ConnectionStrings:Default"]=settings.ConnectionString}).Build());
        services.AddSingleton<ICurrentUser>(new TestUser{UserId=Actor});services.AddSingleton<ICorrelationContext>(new Correlation());
        services.AddInfrastructureServices();services.AddApplicationServices();await using var provider=services.BuildServiceProvider();
        async Task<T> Run<T>(Func<IServiceProvider,Task<T>> action){await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider);}
        var created=await Run(sp=>sp.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(new(){CommandId=Guid.NewGuid(),CustomerId=Customer,
            SupplierId=Supplier,OrderDate=DateTime.Today,Lines=[new(){ProductId=Product,Quantity=4,UnitPrice=2,TaxPrice=2.5m}]}));Assert.IsTrue(created.Success,created.Message);
        var order=created.Data;var line=order.Lines.Single();
        var candidates=await Run(sp=>sp.GetRequiredService<IProcurementDemandAssociationService>().SearchCandidatesAsync(order.PurchaseOrderId,
            new(){PurchaseOrderLineId=line.PurchaseOrderLineId,SourceType="MD",SourceUid=source}));Assert.IsTrue(candidates.Success,candidates.Message);
        Assert.AreEqual(10m,candidates.Data.Items.Single().CreatableQty);Assert.AreEqual(0m,candidates.Data.Items.Single().DraftOccupiedQty);
        var item=new ProcurementDemandAssociationItem{CommandItemId=Guid.NewGuid(),PurchaseOrderLineId=line.PurchaseOrderLineId,ExpectedLineVersion=line.Version,
            NetDemandId=demand,ExpectedDemandVersion=1,ExpectedSourceDemandVersion=1,Quantity=4};
        var preview=await Run(sp=>sp.GetRequiredService<IProcurementDemandAssociationService>().PreviewAsync(order.PurchaseOrderId,new(){ExpectedVersion=order.Version,Items=[item]}));Assert.IsTrue(preview.Success,preview.Message);
        var associated=await Run(sp=>sp.GetRequiredService<IProcurementDemandAssociationService>().AssociateAsync(order.PurchaseOrderId,
            new(){CommandId=Guid.NewGuid(),ExpectedVersion=order.Version,PreviewToken=preview.Data.Token,Reason="专库草稿补关联",Items=[item]}));Assert.IsTrue(associated.Success,associated.Message);
        var sourceCandidates=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCandidateService>().GetCandidatesAsync(new(){SourceType="MD",SourceUid=source}));
        Assert.IsTrue(sourceCandidates.Success,sourceCandidates.Message);var candidate=sourceCandidates.Data.Single();
        Assert.AreEqual(0m,candidate.PurchasedQty);Assert.AreEqual(4m,candidate.DraftOccupiedQty);Assert.AreEqual(6m,candidate.CreatableQty);Assert.AreEqual(10m,candidate.UnpurchasedQty);
        Assert.AreEqual(order.PurchaseOrderId,candidate.DraftOrders.Single().PurchaseOrderId);
        var sourceGroups=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCandidateService>().GetCandidateSourceGroupsAsync());
        Assert.IsTrue(sourceGroups.Success,sourceGroups.Message);Assert.IsTrue(sourceGroups.Data.Any(group=>group.SourceUid==source));
        var safe=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCandidateService>().GetAppendCandidatesAsync("MD",source,Supplier));
        Assert.IsTrue(safe.Success,safe.Message);Assert.AreEqual(order.PurchaseOrderId,safe.Data.Single().PurchaseOrderId);
        var manager=await Run(sp=>sp.GetRequiredService<IPurchaseOrderManagerQueryService>().SearchAsync(new(){Keyword=order.OrderNo}));
        Assert.IsTrue(manager.Success,manager.Message);Assert.IsTrue(manager.Data.Items.Single().CanDeleteDraft);
        var purchaseRows=await Run(sp=>sp.GetRequiredService<IDemandTrackingPurchaseRepository>().GetPurchasesAsync(source));
        Assert.AreEqual(4m,purchaseRows.Single().DraftOccupiedQty);Assert.AreEqual(0m,purchaseRows.Single().PurchasedQty);
        var receiptRows=await Run(sp=>sp.GetRequiredService<IDemandTrackingReceiptRepository>().GetMaterialReceiptsAsync(demand));Assert.IsEmpty(receiptRows!.PurchaseLines);
        var keys=new[]{new OrderTrackingSourceKeyDto{SourceType="MD",SourceGroupId=source}};
        var purchaseSummary=await Run(sp=>sp.GetRequiredService<IOrderTrackingSummaryRepository>().ReadDimensionAsync(keys,"Purchase",DateTime.Today));
        Assert.AreEqual(1,purchaseSummary.Single().PurchaseShortfallMaterialCount);Assert.AreEqual("Unallocated",purchaseSummary.Single().PurchaseStatus);
        var receiptSummary=await Run(sp=>sp.GetRequiredService<IOrderTrackingSummaryRepository>().ReadDimensionAsync(keys,"Receipt",DateTime.Today));
        Assert.AreEqual(0,receiptSummary.Single().ReceiptPurchaseLineCount);Assert.AreEqual(0,receiptSummary.Single().ReceiptOutstandingLineCount);
        var shortfall=await Run(sp=>sp.GetRequiredService<IOrderTrackingSourceRepository>().SearchSourcesAsync(
            new(){SourceTypes=["MD"],Keyword="PDT-Q-"+source.ToString("N"),AttentionCodes=["PurchaseShortfall"]},DateTime.Today));
        Assert.AreEqual(source,shortfall.Items.Single().SourceGroupId);
        var outstanding=await Run(sp=>sp.GetRequiredService<IOrderTrackingSourceRepository>().SearchSourcesAsync(
            new(){SourceTypes=["MD"],Keyword="PDT-Q-"+source.ToString("N"),AttentionCodes=["ReceiptOutstanding"]},DateTime.Today));
        Assert.AreEqual(0,outstanding.TotalCount);
        var rollback=await Run(sp=>sp.GetRequiredService<IMaterialDemandReversalService>().RollbackAsync(new(){CommandId=Guid.NewGuid(),Reason="草稿必须阻断回退",
            Items=[new(){GrossDemandId=source,DecompositionRevision=1}]}));Assert.IsFalse(rollback.Success);
        Assert.AreEqual(DemandPlanningErrorCodes.DownstreamExecutionExists,rollback.Error.Code);StringAssert.Contains(rollback.Message,source.ToString());
        Assert.AreEqual(0,await connection.ExecuteScalarAsync<int>("SELECT lifecycle_status FROM public.net_demand WHERE uid=@Uid",new{Uid=demand}));
        // 关闭保留已经确认的承诺，正式收货/箱单门禁可继续要求活动状态。
        var basis=await Run(sp=>sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(order.PurchaseOrderId));Assert.IsTrue(basis.Success,basis.Message);
        var confirmed=await Run(sp=>sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(order.PurchaseOrderId,
            new(){CommandId=Guid.NewGuid(),ExpectedVersion=basis.Data.Version,ContextFingerprint=basis.Data.Fingerprint}));Assert.IsTrue(confirmed.Success,confirmed.Message);
        await connection.ExecuteAsync("UPDATE public.purchase_order_head SET lifecycle_status=8,version=version+1 WHERE uid=@Uid",new{Uid=order.PurchaseOrderId});
        var material=await Run(sp=>sp.GetRequiredService<IMaterialDemandQueryRepository>().SearchManagerAsync(new(){NetDemandIds=[demand]}));
        Assert.AreEqual(4m,material.Items.Single().PurchasedQty);Assert.AreEqual(0m,material.Items.Single().DraftOccupiedQty);Assert.AreEqual(6m,material.Items.Single().CreatableQty);
    }

    /// <summary>正式服务创建、确认、取消、删除、作废和跨scope竞争共同证明数量守恒与删除后幂等。</summary>
    [TestMethod]
    public async Task RealPg_DraftOccupationLifecycleAndCapacity()
    {
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_RUN_PURCHASE_DRAFT_PG")!="1")
            Assert.Inconclusive("未启用本轮获批采购草稿专库门禁，本项未运行。");
        var root=Root();
        var configuration=new ConfigurationBuilder().AddJsonFile(Path.Combine(root,"SwiftExport.WebApi/appsettings.json")).Build();
        var settings=new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Default"));
        Assert.AreEqual("192.168.3.12",settings.Host);Assert.AreEqual(5432,settings.Port);
        settings.Database=Database;settings.SearchPath="public";settings.Pooling=false;
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_INIT_PURCHASE_DRAFT_PG")=="1")
            await InitializeAsync(settings,root);
        await using var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();
        Assert.AreEqual(Database,connection.Database);
        Assert.AreEqual(2,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_user WHERE uid=ANY(@Ids)",new{Ids=new[]{Actor,DeniedActor}}));
        if(Environment.GetEnvironmentVariable("SWIFTEXPORT_COMPLETE_PURCHASE_DRAFT_PERMISSION_SEED")=="1")
            await SeedPermissionsAsync(connection);
        var services=new ServiceCollection();services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {["ConnectionStrings:Default"]=settings.ConnectionString}).Build());
        var user=new TestUser{UserId=Actor};services.AddSingleton<ICurrentUser>(user);services.AddSingleton<ICorrelationContext>(new Correlation());
        services.AddInfrastructureServices();services.AddApplicationServices();
        await using var provider=services.BuildServiceProvider();
        async Task<T> Run<T>(Func<IServiceProvider,Task<T>> action)
        { await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider); }
        async Task<CreatePurchaseDraftSourceResult> Create(CreatePurchaseDraftsCommand command)
        {var result=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(command));Assert.IsTrue(result.Success,result.Message);return result.Data.Sources.Single();}
        async Task<MaterialDemandManagerItemDto> Material(Guid id,bool details=true)
        {var page=await Run(sp=>sp.GetRequiredService<IMaterialDemandQueryRepository>().SearchManagerAsync(new(){NetDemandIds=[id]},default,details));return page.Items.Single();}
        async Task<(Guid Source,Guid Demand)> Demand()
        {
            var source=Guid.NewGuid();var demand=Guid.NewGuid();
            await connection.ExecuteAsync("""
                INSERT INTO public.gross_demand(uid,source_type,source_uid,source_line_id,source_no,customer_id,product_id,
                    sku_snapshot,unit_snapshot,demand_qty,demand_basis_fingerprint,lifecycle_status,is_non_standard)
                VALUES(@Source,'MD',@Source,@Source,@No,@Customer,@Product,'PDT-FG','PCS',10,@Fingerprint,0,FALSE);
                INSERT INTO public.net_demand(uid,gross_demand_id,source_demand_version_snapshot,source_demand_basis_fingerprint_snapshot,
                    decomposition_revision,line_number,root_net_demand_id,product_id,sku_snapshot,material_type_snapshot,supply_type_snapshot,
                    unit_snapshot,gross_qty,net_qty,lifecycle_status,delivery_mode)
                VALUES(@Demand,@Source,1,@Fingerprint,1,1,@Demand,@Product,'PDT-FG','成品','采购','PCS',10,10,0,0);
                """,new{Source=source,Demand=demand,No="PDT-"+source.ToString("N"),Customer,Product,Fingerprint=new string('A',64)});
            return(source,demand);
        }
        CreatePurchaseDraftsCommand Command((Guid Source,Guid Demand) demand,decimal qty)=>new()
        {ExecutionRequestId=Guid.NewGuid(),EntryMode=PurchaseDraftEntryModeEnum.ManualSelection,Sources=[new(){SourceType="MD",SourceUid=demand.Source,Items=[new(){CommandItemId=Guid.NewGuid(),
            NetDemandId=demand.Demand,ExpectedVersion=1,SupplierId=Supplier,PurchaseQty=qty,UnitPrice=2,TaxPrice=2.5m}]}]};

        var first=await Demand();var command=Command(first,4);
        user.UserId=DeniedActor;
        var denied=await Run(sp=>sp.GetRequiredService<IPurchaseDraftCreationService>().CreateAsync(command));
        Assert.IsFalse(denied.Success);Assert.AreEqual("FORBIDDEN",denied.Error.Code);
        user.UserId=Actor;
        var created=await Create(command);Assert.AreEqual(NetDemandCommandOutcomeEnum.Succeeded,created.Outcome,created.Message);
        var order=created.PurchaseDrafts.Single();var material=await Material(first.Demand);
        Assert.AreEqual(0m,material.PurchasedQty);Assert.AreEqual(4m,material.DraftOccupiedQty);Assert.AreEqual(10m,material.UnpurchasedQty);Assert.AreEqual(6m,material.CreatableQty);
        Assert.AreEqual(order.PurchaseOrderId,material.DraftOrders.Single().PurchaseOrderId);
        Assert.IsEmpty((await Material(first.Demand,false)).DraftOrders);
        var tracking=await Run(sp=>sp.GetRequiredService<IDemandTrackingPurchaseRepository>().GetMaterialPurchasesAsync(first.Demand));
        Assert.AreEqual(0m,tracking!.PurchasedQty);Assert.AreEqual(4m,tracking.DraftOccupiedQty);Assert.AreEqual(10m,tracking.RemainingToPurchaseQty);
        var receipts=await Run(sp=>sp.GetRequiredService<IDemandTrackingReceiptRepository>().GetMaterialReceiptsAsync(first.Demand));
        Assert.IsEmpty(receipts!.PurchaseLines,"纯采购草稿不得变成正式待收量。");
        var purchaseLineId=await connection.ExecuteScalarAsync<Guid>("SELECT uid FROM public.purchase_order_line WHERE purchase_order_id=@Uid AND NOT is_deleted",new{Uid=order.PurchaseOrderId});
        var sources=await Run(sp=>sp.GetRequiredService<IInboundSourceRepository>().ReadPostingSourcesAsync([purchaseLineId],default));
        Assert.IsFalse(sources.Single().IsReceivable,"读取快照不等于具有正式收货资格。");
        var replay=await Create(command);Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied,replay.Outcome);
        command.Sources[0].Items[0].UnitPrice=3;
        Assert.AreEqual("COMMAND_CONFLICT",(await Create(command)).ErrorCode);command.Sources[0].Items[0].UnitPrice=2;

        var detail=await Run(sp=>sp.GetRequiredService<IProcurementOrderQueryService>().GetDetailAsync(order.PurchaseOrderId));
        Assert.IsTrue(detail.Data.CanDeleteDraft);
        var versions=detail.Data.Lines.Select(line=>new ProcurementLineVersionDto{PurchaseOrderLineId=line.Uid,Version=line.Version}).ToList();
        var delete=new DeleteProcurementDraftCommand{CommandId=Guid.NewGuid(),ExpectedVersion=detail.Data.Version,Reason="专库删除草稿",Lines=versions};
        var deleted=await Run(sp=>sp.GetRequiredService<IProcurementOrderReversalService>().DeleteDraftAsync(order.PurchaseOrderId,delete));
        Assert.IsTrue(deleted.Success,deleted.Message);Assert.IsTrue(deleted.Data.IsDeleted);Assert.AreEqual(delete.ExpectedVersion+1,deleted.Data.Version);
        Assert.AreEqual(versions.Single().Version+1,deleted.Data.Lines.Single().Version);
        var deleteReplay=await Run(sp=>sp.GetRequiredService<IProcurementOrderReversalService>().DeleteDraftAsync(order.PurchaseOrderId,delete));
        Assert.IsTrue(deleteReplay.Success,deleteReplay.Message);Assert.IsTrue(deleteReplay.Data.IsDeleted);
        Assert.AreEqual(NetDemandCommandOutcomeEnum.AlreadyApplied,(await Create(command)).Outcome);
        Assert.AreEqual(1,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.purchase_order_head WHERE uid=@Uid AND is_deleted",new{Uid=order.PurchaseOrderId}));
        Assert.AreEqual(0m,(await Material(first.Demand)).DraftOccupiedQty);

        var second=await Demand();var confirmDraft=(await Create(Command(second,5))).PurchaseDrafts.Single();
        var basis=await Run(sp=>sp.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(confirmDraft.PurchaseOrderId));Assert.IsTrue(basis.Success,basis.Message);
        var confirmed=await Run(sp=>sp.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(confirmDraft.PurchaseOrderId,
            new(){CommandId=Guid.NewGuid(),ExpectedVersion=basis.Data.Version,ContextFingerprint=basis.Data.Fingerprint}));Assert.IsTrue(confirmed.Success,confirmed.Message);
        material=await Material(second.Demand);Assert.AreEqual(5m,material.PurchasedQty);Assert.AreEqual(0m,material.DraftOccupiedQty);Assert.AreEqual(5m,material.CreatableQty);
        var cancelled=await Run(sp=>sp.GetRequiredService<IProcurementOrderReversalService>().CancelConfirmationAsync(confirmDraft.PurchaseOrderId,
            new(){CommandId=Guid.NewGuid(),ExpectedVersion=confirmed.Data.Version,Reason="专库取消确认"}));Assert.IsTrue(cancelled.Success,cancelled.Message);
        material=await Material(second.Demand);Assert.AreEqual(0m,material.PurchasedQty);Assert.AreEqual(5m,material.DraftOccupiedQty);Assert.AreEqual(5m,material.CreatableQty);
        detail=await Run(sp=>sp.GetRequiredService<IProcurementOrderQueryService>().GetDetailAsync(confirmDraft.PurchaseOrderId));Assert.IsFalse(detail.Data.CanDeleteDraft);
        var forbiddenDelete=await Run(sp=>sp.GetRequiredService<IProcurementOrderReversalService>().DeleteDraftAsync(confirmDraft.PurchaseOrderId,
            new(){CommandId=Guid.NewGuid(),ExpectedVersion=detail.Data.Version,Reason="不能掩盖确认历史",Lines=detail.Data.Lines.Select(line=>new ProcurementLineVersionDto{PurchaseOrderLineId=line.Uid,Version=line.Version}).ToList()}));
        Assert.IsFalse(forbiddenDelete.Success);
        var voided=await Run(sp=>sp.GetRequiredService<IProcurementOrderReversalService>().VoidAsync(confirmDraft.PurchaseOrderId,
            new(){CommandId=Guid.NewGuid(),ExpectedVersion=detail.Data.Version,Reason="专库作废释放"}));Assert.IsTrue(voided.Success,voided.Message);
        Assert.AreEqual(10m,(await Material(second.Demand)).CreatableQty);

        // 两个独立request scope竞争同一需求，真实来源租约及锁不得允许各自承接6而累计成12。
        var race=await Demand();var attempts=await Task.WhenAll(Create(Command(race,6)),Create(Command(race,6)));
        Assert.AreEqual(1,attempts.Count(result=>result.Outcome==NetDemandCommandOutcomeEnum.Succeeded));
        material=await Material(race.Demand);Assert.AreEqual(6m,material.DraftOccupiedQty);Assert.AreEqual(4m,material.CreatableQty);
        // 成立身份保护不允许篡改已有Link；直接新增逻辑孤立关系模拟外部坏数据，不放松触发器或外键规则。
        await connection.ExecuteAsync("""
            INSERT INTO public.demand_execution_link(uid,net_demand_id,execution_request_id,command_item_id,target_type,
                target_head_id,target_line_id,target_no,link_status,allocated_qty,released_qty)
            VALUES(gen_random_uuid(),@Uid,gen_random_uuid(),gen_random_uuid(),'PO',@Missing,gen_random_uuid(),'PDT-MISSING',0,1,0);
            """,new{Missing=Guid.NewGuid(),Uid=race.Demand});
        material=await Material(race.Demand);Assert.IsNull(material.CreatableQty);Assert.IsFalse(string.IsNullOrEmpty(material.ReviewReason));
        var blocked=await Create(Command(race,1));Assert.AreEqual(NetDemandCommandOutcomeEnum.Failed,blocked.Outcome);StringAssert.Contains(blocked.Message,"核实");
        Directory.CreateDirectory(Path.Combine(root,"SwiftExport.Application.Tests/TestResults"));
        await File.WriteAllTextAsync(Path.Combine(root,"SwiftExport.Application.Tests/TestResults/purchase-draft-lifecycle-pg-evidence.json"),
            System.Text.Json.JsonSerializer.Serialize(new{Host=settings.Host,Port=settings.Port,Database,ReadOnly=false,CreatedDraft=order.PurchaseOrderId,
                Lifecycle=true,ReplayAfterDelete=true,ConcurrencyCapacity=true,UnknownCapacityBlocked=true,NoCleanup=true}));
    }

    /// <summary>只允许首次创建不存在的新专库并初始化当前结构与本轮种子；重复初始化停止，不清理任何库。</summary>
    private static async Task InitializeAsync(NpgsqlConnectionStringBuilder settings,string root)
    {
        var maintenance=new NpgsqlConnectionStringBuilder(settings.ConnectionString){Database="postgres"};
        await using var admin=new NpgsqlConnection(maintenance.ConnectionString);await admin.OpenAsync();
        Assert.IsFalse(await admin.ExecuteScalarAsync<bool>("SELECT EXISTS(SELECT 1 FROM pg_database WHERE datname=@Name)",new{Name=Database}),"专库已存在，拒绝初始化；不得删除或重置。");
        await admin.ExecuteAsync("CREATE DATABASE dv_purchase_draft_lifecycle_tests_20261020");
        await using var connection=new NpgsqlConnection(settings.ConnectionString);await connection.OpenAsync();Assert.AreEqual(Database,connection.Database);
        var schema=await File.ReadAllTextAsync(Path.Combine(root,"SwiftExport.Infrastructure/Database/Generated/SwiftExport.PostgreSql.Schema.preview.sql"));
        await connection.ExecuteAsync(schema,commandTimeout:120);
        await connection.ExecuteAsync(await File.ReadAllTextAsync(Path.Combine(root,"SwiftExport.Application.Tests/Database/Fixtures/采购草稿语义专库初始化.sql")),commandTimeout:60);
        await SeedPermissionsAsync(connection);
    }

    /// <summary>仅补齐本专库缺失权限和本批角色关联；一个事务执行，不覆盖已有权限、账号或任何业务事实。</summary>
    private static async Task SeedPermissionsAsync(NpgsqlConnection connection)
    {
        Assert.AreEqual(Database,connection.Database);
        Assert.AreEqual(1,await connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.sys_role WHERE uid='20261020-0000-0000-0000-000000000001' AND role_code='PURCHASE_DRAFT_TEST'"));
        await using var transaction=await connection.BeginTransactionAsync();
        var keys=new[]{PermissionKeys.ProcurementOrderCreate,PermissionKeys.ProcurementOrderRead,PermissionKeys.ProcurementOrderEdit,
            PermissionKeys.ProcurementOrderConfirm,PermissionKeys.ProcurementOrderVoid,PermissionKeys.DemandPlanningMaterialView,PermissionKeys.DemandPlanningMaterialRollback,
            PermissionKeys.OrderTrackingView,PermissionKeys.InventoryInboundView};
        foreach(var key in keys)
        {
            Assert.IsNotNull(PermissionCatalog.Find(key));
            await connection.ExecuteAsync("""
                INSERT INTO public.sys_permission(uid,permission_key,display_name,group_key,sort_order,is_active)
                    SELECT gen_random_uuid(),@Key,@Key,'PurchaseDraftTest',0,TRUE
                    WHERE NOT EXISTS(SELECT 1 FROM public.sys_permission WHERE permission_key=@Key);
                INSERT INTO public.sys_role_permission(uid,role_id,permission_id)
                    SELECT gen_random_uuid(),'20261020-0000-0000-0000-000000000001',permission.uid FROM public.sys_permission permission
                    WHERE permission.permission_key=@Key AND NOT EXISTS(SELECT 1 FROM public.sys_role_permission relation
                        WHERE relation.role_id='20261020-0000-0000-0000-000000000001' AND relation.permission_id=permission.uid);
                """,new{Key=key},transaction);
        }
        await transaction.CommitAsync();
    }

    /// <summary>从测试运行目录向上定位本工作树，不读取其他工作树的配置或结果。</summary>
    private static string Root()
    {var current=new DirectoryInfo(AppContext.BaseDirectory);while(current is not null&&!File.Exists(Path.Combine(current.FullName,"AGENTS.md")))current=current.Parent;return current?.FullName??throw new InvalidOperationException("找不到本工作树。");}
    /// <summary>真实权限服务仍从数据库检查账号和非管理员角色，只替换请求身份而不绕开授权。</summary>
    private sealed class TestUser:ICurrentUser
    {public Guid? UserId{get;set;}public string UserName=>"purchase-draft-test";public bool IsAuthenticated=>true;}
    /// <summary>专库审计采用固定可检索的关联标识。</summary>
    private sealed class Correlation:ICorrelationContext{public string CorrelationId=>"purchase-draft-lifecycle-20261020";}
}

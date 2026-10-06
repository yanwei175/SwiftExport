using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Application.Tests.Database;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Infrastructure;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>在获批单PG测试库复用原采购夹具的主数据与真实服务；释放全部scope后导出事实并限定重置。</summary>
internal sealed class IndependentProcurementPostgreSqlFixture : IAsyncDisposable
{
    private PostgreSqlLegacyTestDatabase? database;
    private ServiceProvider? provider;
    public Guid ActorId { get; set; } = Guid.NewGuid();
    public Guid OtherActorId { get; } = Guid.NewGuid();
    public Guid CustomerId { get; } = Guid.NewGuid();
    public Guid SupplierId { get; } = Guid.NewGuid();
    public Guid ReceiptSupplierId { get; } = Guid.NewGuid();
    public Guid ConsumptionSupplierId { get; } = Guid.NewGuid();
    public Guid ReceiptSiteId { get; } = Guid.NewGuid();
    public Guid ConsumptionSiteId { get; } = Guid.NewGuid();
    public Guid ProductId { get; } = Guid.NewGuid();
    public Guid ComponentId { get; } = Guid.NewGuid();
    public Guid BomLineId { get; } = Guid.NewGuid();
    public Guid RoleId { get; } = Guid.NewGuid();

    /// <summary>获取串行租约、确认空库并插入原14条主数据；真实DI及业务服务保持当前PG接线。</summary>
    public async Task InitializeAsync(TestContext context)
    {
        database = await PostgreSqlLegacyTestDatabase.AcquireAsync(context);
        await SeedAsync();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["ConnectionStrings:Default"] = database.ConnectionString }).Build());
        services.AddSingleton<ICurrentUser>(new FixtureUser(this));
        services.AddSingleton<ICorrelationContext>(new FixtureCorrelation());
        services.AddInfrastructureServices();
        services.AddApplicationServices();
        provider = services.BuildServiceProvider();
    }

    /// <summary>现行实体基线已包含反向所需结构，不追加旧提供者迁移。</summary>
    public Task PrepareReversalSchemaAsync() => Task.CompletedTask;

    /// <summary>每次调用独立真实request scope，服务与仓储仍共享同一事务会话。</summary>
    public async Task<T> InScopeAsync<T>(Func<IServiceProvider, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider);
    }

    /// <summary>通过原草稿服务创建独立采购并验证真实业务结果。</summary>
    public Task<ServiceResult<ProcurementOrderCommandResult>> CreateAsync(CreateIndependentPurchaseOrderCommand? command = null)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderDraftService>().CreateAsync(command ?? CreateCommand()));

    /// <summary>通过原草稿服务保存头行快照及版本。</summary>
    public Task<ServiceResult<ProcurementOrderCommandResult>> SaveAsync(Guid uid, UpdatePurchaseOrderDraftCommand command)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderDraftService>().SaveAsync(uid, command));

    /// <summary>通过原确认服务复核依据并执行确认。</summary>
    public Task<ServiceResult<ProcurementOrderCommandResult>> ConfirmAsync(Guid uid, ConfirmProcurementOrderCommand command)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderConfirmationService>().ConfirmAsync(uid, command));

    /// <summary>使用真实事务和确认构建器，不在测试夹具内重算指纹。</summary>
    public Task<ServiceResult<ProcurementConfirmationContextDto>> ContextAsync(Guid uid)
        => InScopeAsync(p => p.GetRequiredService<IProcurementOrderConfirmationService>().GetConfirmationContextAsync(uid));

    /// <summary>原默认采购输入保持日期、数量和价格规则。</summary>
    public CreateIndependentPurchaseOrderCommand CreateCommand() => new()
    {
        CommandId = Guid.NewGuid(), CustomerId = CustomerId, SupplierId = SupplierId,
        OrderDate = new DateTime(2026, 8, 31), PackagingRequirements = "客户包装要求",
        Lines = [new() { ProductId = ProductId, Quantity = 10m, UnitPrice = 2m, TaxPrice = 2.5m }]
    };

    /// <summary>只为原需要BOM的场景写入本例组成及绑定，不复制历史BOM。</summary>
    public async Task AddBomAsync() => await ExecuteAsync("""
        UPDATE public.product SET bom_no='TEST-BOM',bom_version=1,version=version+1 WHERE uid=@ProductId;
        INSERT INTO public.bom(uid,bom_no,bom_version,parent_product_id,parent_sku_snapshot,line_number,component_product_id,component_sku_snapshot,numerator,denominator)
        VALUES(@BomLineId,'TEST-BOM',1,@ProductId,'TEST-FG',1,@ComponentId,'TEST-B',2,3);
        """, new { ProductId, ComponentId, BomLineId });

    /// <summary>参数化执行当前场景的PG夹具SQL；连接仅指向本任务新库。</summary>
    public async Task<int> ExecuteAsync(string sql, object? args = null)
    {
        await using var connection = await OpenConnectionAsync();
        return await connection.ExecuteAsync(sql, args, commandTimeout: 90);
    }

    /// <summary>在新库查询原测试断言需要的标量，不替换或模拟仓储返回。</summary>
    public async Task<T> ScalarAsync<T>(string sql, object? args = null)
    {
        await using var connection = await OpenConnectionAsync();
        return (await connection.ExecuteScalarAsync<T>(sql, args))!;
    }

    /// <summary>向真实锁等待测试提供同一新库连接，由调用方负责释放。</summary>
    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(database!.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>仅在新库注入实际日志写入异常，保留前序写入与回执全部回滚的原断言。</summary>
    public Task<int> RejectLogsAsync() => ExecuteAsync("""
        CREATE FUNCTION public.p09_test_reject_log() RETURNS trigger LANGUAGE plpgsql AS $$
        BEGIN RAISE EXCEPTION '隔离测试日志失败' USING ERRCODE='P2999'; END $$;
        CREATE TRIGGER tr_test_reject_log AFTER INSERT ON public.business_operation_log
        FOR EACH ROW EXECUTE FUNCTION public.p09_test_reject_log();
        """);

    /// <summary>原测试管理员、客户、供应商、产品、单位和仓库数据仅按PG物理名及bool翻译。</summary>
    private Task<int> SeedAsync() => ExecuteAsync("""
        INSERT INTO public.sys_role(uid,role_code,role_name,is_active,is_system) VALUES(@RoleId,'SYSTEM_ADMIN','隔离测试管理员',TRUE,TRUE);
        INSERT INTO public.sys_user(uid,user_name,password_hash,credential_version,is_active) VALUES(@ActorId,'sql-test-admin','test-only',1,TRUE),(@OtherActorId,'sql-test-admin-2','test-only',1,TRUE);
        INSERT INTO public.sys_user_role(uid,user_id,role_id) VALUES(gen_random_uuid(),@ActorId,@RoleId),(gen_random_uuid(),@OtherActorId,@RoleId);
        INSERT INTO public.customer(uid,customer_code,customer_name,doc_relate_no,deposit_rate,lead_time_days,is_active) VALUES(@CustomerId,'TEST-C','归属客户','TEST-C',0,0,TRUE);
        INSERT INTO public.supplier(uid,supplier_code,supplier_name,doc_relate_no,is_active) VALUES
          (@SupplierId,'TEST-ACTUAL','实际供应商','TEST-ACTUAL',TRUE),(@ReceiptSupplierId,'TEST-RECEIPT','收货供应商','TEST-RECEIPT',TRUE),
          (@ConsumptionSupplierId,'TEST-CONSUME','消耗供应商','TEST-CONSUME',TRUE);
        INSERT INTO public.product(uid,sku,description_zh,unit,material_type,supply_type,bom_version,color_id,is_active) VALUES
          (@ProductId,'TEST-FG','采购成品','PCS','成品','采购',0,@EmptyId,TRUE),
          (@ComponentId,'TEST-B','客供件','PCS','客供件','采购',0,@EmptyId,TRUE);
        INSERT INTO public.sys_dict_data(uid,dict_code,data_name_en,is_system,is_enabled) VALUES(gen_random_uuid(),'Unit','PCS',TRUE,TRUE);
        INSERT INTO public.warehouse_site(uid,code,name,site_type,supplier_id,is_active) VALUES
          (@ReceiptSiteId,'TEST-R','收货仓',0,@ReceiptSupplierId,TRUE),(@ConsumptionSiteId,'TEST-C','消耗仓',0,@ConsumptionSupplierId,TRUE);
        """, new { ActorId, OtherActorId, RoleId, CustomerId, SupplierId, ReceiptSupplierId, ConsumptionSupplierId, ProductId, ComponentId, ReceiptSiteId, ConsumptionSiteId, EmptyId = Guid.Empty });

    /// <summary>先关闭服务容器及所有作用域，再交给单库管理器导出和重置；不删除数据库本身。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider != null) await provider.DisposeAsync();
        if (database != null) await database.DisposeAsync();
    }

    /// <summary>使用原夹具身份，不建立PG登录角色或修改既有权限。</summary>
    private sealed class FixtureUser(IndependentProcurementPostgreSqlFixture fixture) : ICurrentUser
    {
        public string UserName => "sql-test-admin";
        public Guid? UserId => fixture.ActorId;
        public bool IsAuthenticated => true;
    }

    /// <summary>为隔离测试提供固定关联标识，不修改业务日志契约。</summary>
    private sealed class FixtureCorrelation : ICorrelationContext
    {
        public string CorrelationId => "isolated-sql-test";
    }
}

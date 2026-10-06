using Dapper;
using Npgsql;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Tests.Database;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Inventory.Query;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
internal sealed class InventoryRepositorySqlFixture : IAsyncDisposable
{
    private PostgreSqlLegacyTestDatabase? database;
    private ServiceProvider? provider;

    /// <summary>测试供应商身份。</summary>
    public Guid SupplierId { get; } = Guid.NewGuid();

    /// <summary>测试虚拟仓身份。</summary>
    public Guid WarehouseId { get; } = Guid.NewGuid();

    /// <summary>测试余额身份集合，顺序与测试数据定义一致。</summary>
    public IReadOnlyList<Guid> BalanceIds { get; private set; } = [];

    /// <summary>测试产品身份集合，顺序与测试数据定义一致。</summary>
    public IReadOnlyList<Guid> ProductIds { get; private set; } = [];


    /// <summary>在固定PG基线准备原库存查询与特殊字符样本。</summary>
    public async Task InitializeAsync(TestContext context)
    {
        database = await PostgreSqlLegacyTestDatabase.AcquireAsync(context);


        var samples = new[]
        {
            new Sample("SKU-普通", "DOC-普通"),
            new Sample("SKU-%-百分号", "DOC-%-百分号"),
            new Sample("SKU-_-下划线", "DOC-_-下划线"),
            new Sample("SKU-[-方括号", "DOC-[-方括号"),
            new Sample(@"SKU-\-反斜杠", @"DOC-\-反斜杠")
        };
        BalanceIds = samples.Select(_ => Guid.NewGuid()).ToArray();
        ProductIds = samples.Select(_ => Guid.NewGuid()).ToArray();
        await ExecuteAsync("""
            INSERT INTO public.supplier(uid,supplier_code,supplier_name,is_active,is_deleted,doc_relate_no)
            VALUES(@SupplierId,'SUP-QUERY','库存查询供应商',TRUE,FALSE,'TEST');
            INSERT INTO public.warehouse_site(uid,version,is_deleted,code,name,site_type,supplier_id,supplier_code_snapshot,supplier_name_snapshot,is_active)
            VALUES(@WarehouseId,1,FALSE,'WH-QUERY','库存查询仓',0,@SupplierId,'SUP-QUERY','库存查询供应商',TRUE);
            """, new { SupplierId, WarehouseId });

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            var productId = ProductIds[index];
            if (index <= 1)
            {
                await ExecuteAsync("INSERT INTO public.product(uid,is_deleted,main_image,sku,supply_type,bom_version,is_active) VALUES(@ProductId,@IsDeleted,@MainImage,@Sku,'采购',0,TRUE);", new
                {
                    ProductId = productId, Sku = sample.Sku,
                    IsDeleted = index == 1,
                    MainImage = $"images/product-{index + 1}.png"
                });
            }
            await ExecuteAsync("""
                INSERT INTO public.inventory_balance
                    (uid,version,is_deleted,updated_date,warehouse_site_id,product_id,sku_snapshot,product_name_snapshot,unit_snapshot,on_hand_qty,last_ledger_sequence)
                VALUES(@BalanceId,1,FALSE,LOCALTIMESTAMP,@WarehouseId,@ProductId,@Sku,'库存查询产品','PCS',@Quantity,@Sequence);
                INSERT INTO public.inventory_ledger_entry
                    (uid,version,is_deleted,created_date,created_by,sequence,warehouse_site_id,product_id,transaction_type,quantity_delta,balance_after,
                     source_document_type,source_document_id,source_line_id,source_document_no_snapshot,command_id,command_item_id,occurred_date,
                     warehouse_code_snapshot,warehouse_name_snapshot,sku_snapshot,product_name_snapshot,unit_snapshot)
                VALUES(gen_random_uuid(),1,FALSE,LOCALTIMESTAMP,'隔离测试',@Sequence,@WarehouseId,@ProductId,10,@Quantity,@Quantity,
                       'InventoryQueryTest',gen_random_uuid(),gen_random_uuid(),@DocumentNo,gen_random_uuid(),gen_random_uuid(),LOCALTIMESTAMP,
                       'WH-QUERY','库存查询仓',@Sku,'库存查询产品','PCS');
                """, new
            {
                BalanceId = BalanceIds[index], WarehouseId, ProductId = productId, sample.Sku,
                sample.DocumentNo, Quantity = index + 1m, Sequence = index + 1L
            });
        }

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = database!.ConnectionString
        }).Build());
        services.AddSingleton<ICurrentUser>(new FixtureUser());
        SwiftExport.Infrastructure.DependencyInjection.AddInfrastructureServices(services);
        provider = services.BuildServiceProvider();
    }

    /// <summary>在独立作用域中调用真实库存仓储，确保 count 和 page SQL 均由 PostgreSQL 执行。</summary>
    public async Task<T> QueryAsync<T>(Func<IInventoryQueryRepository, Task<T>> action)
    {
        await using var scope = provider!.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<IInventoryQueryRepository>());
    }

    /// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
    public async Task<int> ExecuteAsync(string sql, object? parameters = null)
    {
        await using var connection = new NpgsqlConnection(database!.ConnectionString);
        await connection.OpenAsync();
        return await connection.ExecuteAsync(sql, parameters);
    }

    /// <summary>复用获批单PG测试库及原场景数据；释放连接、完整导出后仅重置该新库public。</summary>
    public async ValueTask DisposeAsync()
    {
        if (provider is not null) await provider.DisposeAsync();
        if (database is not null) await database.DisposeAsync();
    }

    private sealed record Sample(string Sku, string DocumentNo);

    private sealed class FixtureUser : ICurrentUser
    {
        public string UserName => "库存查询SQL测试";
        public Guid? UserId => null;
        public bool IsAuthenticated => true;
    }
}

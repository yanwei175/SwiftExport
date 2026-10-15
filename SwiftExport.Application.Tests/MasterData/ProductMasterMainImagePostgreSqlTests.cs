using System.Diagnostics;
using System.Reflection;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;
using SwiftExport.Application.Services.Products.ProductMasters.Maintenance;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Products.ProductMasters.Requests;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.MasterData;
using SwiftExport.Core.Interfaces.Products.Colors;
using SwiftExport.Core.Interfaces.Products.ProductMasters;
using SwiftExport.Core.Interfaces.Products.ProductNeutrals;

namespace SwiftExport.Application.Tests.MasterData;

/// <summary>仅在已授权复制库验证颜色数据库排序、竞争写入和真实回滚；只清理本用例夹具。</summary>
[TestClass, DoNotParallelize, TestCategory("ProductAttributePostgreSqlIntegration")]
public sealed class ProductMasterMainImagePostgreSqlTests
{
    /// <summary>PG 颜色排序和同色编码顺序决定候选；两个请求竞争时只提交一次默认值和版本。</summary>
    [TestMethod]
    public async Task ColorOrderAndConcurrentRequests_CommitOneDefault()
    {
        await using var f = await ProductAttributePostgreSqlFixture.CreateAsync("dv_next_product_main_image_tests_20261015"); var setup = f.NewScope();
        var later = await AddColorAsync(f, setup, "LATE", 9);
        var tiedLater = await AddColorAsync(f, setup, "Z", 1);
        var first = await AddColorAsync(f, setup, "A", 1);
        await AddNeutralAsync(f, setup, f.Master, later, "A");
        await AddNeutralAsync(f, setup, f.Master, tiedLater, "B");
        await AddNeutralAsync(f, setup, f.Master, first, "Z");
        var winner = await AddNeutralAsync(f, setup, f.Master, first, "C");
        var request = Request(f.Master);
        var a = f.NewScope(); var b = f.NewScope();
        var results = await Task.WhenAll(a.Maintenance.SetMainImagesAsync(request), b.Maintenance.SetMainImagesAsync(request)).WaitAsync(TimeSpan.FromSeconds(25));
        Assert.AreEqual(1, results.Count(result => result.Success));
        Assert.AreEqual(ServiceErrorCodes.VersionConflict, results.Single(result => !result.Success).Error.Code);
        Assert.AreEqual(winner.Uid, results.Single(result => result.Success).Data.Items.Single().ProductNeutralId);
        Assert.AreEqual(winner.Uid, await setup.Session.Connection.QuerySingleAsync<Guid>(
            "SELECT uid FROM public.product_neutral WHERE product_master_id=@Id AND is_main_image=TRUE AND is_deleted=FALSE", new { Id = f.Master.Uid }));
        var savedVersion = await setup.Session.Connection.ExecuteScalarAsync<int>("SELECT version FROM public.product_master WHERE uid=@Id", new { Id = f.Master.Uid });
        Assert.AreEqual(2, savedVersion);
        request.Items[0].ExpectedVersion = savedVersion;
        var skipped = await setup.Maintenance.SetMainImagesAsync(request);
        Assert.IsTrue(skipped.Success, skipped.Message); Assert.AreEqual(1, skipped.Data.AlreadySetCount);
        Assert.IsEmpty(skipped.Data.Items);
    }

    /// <summary>第二个主型号写入失败后，真实事务撤销前一个主型号版本及所有已写入的中性默认标记。</summary>
    [TestMethod]
    public async Task LaterWriteFailure_RollsBackEarlierDatabaseWrites()
    {
        await using var f = await ProductAttributePostgreSqlFixture.CreateAsync("dv_next_product_main_image_tests_20261015"); var scope = f.NewScope();
        var other = new ProductMaster { MasterSku = f.CodePrefix + "-M2", CategoryId = f.Category.Uid, DefaultPcsPerPackage = 1 };
        var masterRepository = scope.Services.GetRequiredService<IProductMasterRepository>();
        Assert.IsTrue((await masterRepository.AddAsync(other)).Success); f.MasterIds.Add(other.Uid);
        var color = await AddColorAsync(f, scope, "A", 1);
        await AddNeutralAsync(f, scope, f.Master, color, "A"); await AddNeutralAsync(f, scope, other, color, "B");
        var proxy = DispatchProxy.Create<IProductMasterRepository, MasterProxy>(); var calls = 0;
        ((MasterProxy)(object)proxy).Call = (method, args) => method.Name == nameof(IProductMasterRepository.UpdateAsync) && ++calls == 2
            ? Task.FromResult(RepositoryWriteResult.Fail(((ProductMaster)args[0]!).Uid, 1, ServiceErrorCodes.ConcurrencyConflict, "测试冲突"))
            : method.Invoke(masterRepository, args);
        var service = new ProductMasterMaintenanceService(proxy, scope.Services.GetRequiredService<IProductNeutralRepository>(), null!, null!,
            scope.Services.GetRequiredService<IProductColorRepository>(), null!, null!, new(scope.Session, NullLoggerFactory.Instance));
        var result = await service.SetMainImagesAsync(Request(f.Master, other));
        Assert.IsFalse(result.Success); Assert.AreEqual(ServiceErrorCodes.VersionConflict, result.Error.Code);
        Assert.IsNull(scope.Session.Transaction);
        var ids = new[] { f.Master.Uid, other.Uid };
        Assert.AreEqual(2, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_master WHERE uid=ANY(@Ids) AND version=1", new { Ids = ids }));
        Assert.AreEqual(0, await scope.Session.Connection.ExecuteScalarAsync<int>("SELECT count(*) FROM public.product_neutral WHERE product_master_id=ANY(@Ids) AND (is_main_image=TRUE OR version<>1)", new { Ids = ids }));
    }

    /// <summary>颜色排序正在提交时，主图请求等待颜色锁，锁后使用已提交的新顺序。</summary>
    [TestMethod]
    public async Task ConcurrentColorReorder_UsesOrderAfterColorLock()
    {
        await using var f = await ProductAttributePostgreSqlFixture.CreateAsync("dv_next_product_main_image_tests_20261015"); var scope = f.NewScope();
        var first = await AddColorAsync(f, scope, "A", 1); var second = await AddColorAsync(f, scope, "B", 2);
        await AddNeutralAsync(f, scope, f.Master, first, "A");
        var winner = await AddNeutralAsync(f, scope, f.Master, second, "B");
        await using var owner = new NpgsqlConnection(f.ConnectionString); await owner.OpenAsync();
        await using var transaction = await owner.BeginTransactionAsync();
        await owner.ExecuteAsync("UPDATE public.product_color SET sort_no=100,version=version+1 WHERE uid=@Id", new { Id = first.Uid }, transaction);
        var contender = f.NewScope(); var pending = contender.Maintenance.SetMainImagesAsync(Request(f.Master));
        var timer = Stopwatch.StartNew(); var blocked = false;
        try
        {
            while (timer.Elapsed < TimeSpan.FromSeconds(5))
            {
                blocked = await scope.Session.Connection.ExecuteScalarAsync<bool>("SELECT COALESCE((SELECT wait_event_type='Lock' FROM pg_stat_activity WHERE pid=@Pid),FALSE)",
                    new { Pid = ((NpgsqlConnection)contender.Session.Connection).ProcessID });
                if (blocked) break;
                await Task.Delay(20);
            }
        }
        finally { await transaction.CommitAsync(); }
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(15));
        Assert.IsTrue(blocked, "应观察到颜色锁等待，不能用固定延迟假称已发生并发。");
        Assert.IsTrue(result.Success, result.Message); Assert.AreEqual(winner.Uid, result.Data.Items.Single().ProductNeutralId);
    }

    /// <summary>创建本夹具独有的有效颜色，不改已有颜色、排序或数据库结构。</summary>
    private static async Task<ProductColor> AddColorAsync(ProductAttributePostgreSqlFixture f, ProductAttributePostgreSqlFixture.Scope scope, string suffix, int sort)
    {
        var row = new ProductColor { ColorCodeUnique = f.CodePrefix + "-" + suffix, ColorCode = suffix, ColorNameEn = suffix, ColorNameZh = suffix, SortNo = sort };
        var write = await scope.Services.GetRequiredService<IProductColorRepository>().AddAsync(row);
        Assert.IsTrue(write.Success, write.Message); f.ColorIds.Add(row.Uid); return row;
    }
    /// <summary>创建带独立编号和虚拟图片地址的中性候选，测试不会访问图片服务器。</summary>
    private static async Task<ProductNeutral> AddNeutralAsync(ProductAttributePostgreSqlFixture f, ProductAttributePostgreSqlFixture.Scope scope, ProductMaster master, ProductColor color, string suffix)
    {
        var row = new ProductNeutral { ProductMasterId = master.Uid, ProductCode = f.CodePrefix + "-" + suffix, ColorId = color.Uid, ImagePath = "https://example.test/" + suffix + ".jpg" };
        var write = await scope.Services.GetRequiredService<IProductNeutralRepository>().AddAsync(row);
        Assert.IsTrue(write.Success, write.Message); return row;
    }
    /// <summary>为给定主型号保留明确身份和预期版本。</summary>
    private static BatchSetProductMasterMainImagesRequest Request(params ProductMaster[] masters) => new()
    { Items = masters.Select(row => new ProductMasterMainImageTarget { ProductMasterId = row.Uid, ExpectedVersion = row.Version }).ToList() };
    /// <summary>仅在指定主型号写入点注入冲突，其余调用执行真实仓储及共享事务。</summary>
    public class MasterProxy : DispatchProxy
    {
        /// <summary>当前用例的真实仓储转发和故障注入入口。</summary>
        public Func<MethodInfo, object?[], object?> Call { get; set; } = null!;
        /// <summary>转发当前仓储接口调用。</summary>
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Call(method!, args ?? []);
    }
}

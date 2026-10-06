using Microsoft.Extensions.DependencyInjection;
using SwiftExport.Core.Interfaces.Inventory.Inbounds;
using SwiftExport.Infrastructure;
using SwiftExport.Infrastructure.Contexts;
using SwiftExport.Infrastructure.Repositories.Inventory.Inbounds;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;

namespace SwiftExport.Application.Tests.Inventory;

/// <summary>检查 PG 入库 SQL 边界及关键来源口径，离线检查不冒充真实数据库并发测试。</summary>
[TestClass]
public sealed class InboundPostingSqlContractTests
{
    /// <summary>草稿提示排除本单且不混入正式净额，来源锁在入库锁之前。</summary>
    [TestMethod]
    public void Sql_SeparatesDraftWarningsAndFormalCapacity()
    {
        var root = FindRoot();
        var query = (string)typeof(PostgreSqlInboundQueryRepository).GetMethod("DetailSql", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.Invoke(null, null)!;
        var source = File.ReadAllText(Path.Combine(root, "SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/PostgreSqlInboundSourceRepository.cs"));
        StringAssert.Contains(query, "dh.uid<>@Uid");
        StringAssert.Contains(source, "dh.uid<>@CurrentInboundDocumentId");
        StringAssert.Contains(source, "dh.posting_status=0 AND dh.lifecycle_status=0");
        StringAssert.Contains(query, "rh.posting_status IN(1,8)");
        StringAssert.Contains(source, "COUNT(DISTINCT dh.uid)");
        var service = File.ReadAllText(Path.Combine(root, "SwiftExport.Application/Services/Inventory/Inbounds/InboundPostingService.cs"));
        Assert.IsTrue(service.IndexOf("LockPurchaseSourcesAsync", StringComparison.Ordinal) < service.IndexOf("LockHeaderAsync", StringComparison.Ordinal));
        StringAssert.Contains(service, "inventoryPosting.PostAsync");
        var document = File.ReadAllText(Path.Combine(root, "SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/InboundDocumentRepository.cs"));
        var executionRead = File.ReadAllText(Path.Combine(root, "SwiftExport.Infrastructure/Repositories/Inventory/Inbounds/InboundExecutionReadRepository.cs"));
        foreach (var table in new[] { "public.inventory_balance", "\"public\".\"inventory_balance\"" })
            Assert.IsFalse(source.Contains("UPDATE " + table, StringComparison.OrdinalIgnoreCase));
        foreach (var table in new[] { "public.inventory_ledger_entry", "\"public\".\"inventory_ledger_entry\"" })
            Assert.IsFalse(source.Contains("INSERT INTO " + table, StringComparison.OrdinalIgnoreCase));
        StringAssert.Contains(source, "SELECT uid FROM public.purchase_order_head WHERE uid=@Id FOR UPDATE");
        StringAssert.Contains(source, "SELECT purchase_order_id FROM public.purchase_order_line WHERE uid=@Id FOR UPDATE");
        StringAssert.Contains(document, "WHERE Uid=@Uid AND Version=@Version AND PostingStatus=0");
        StringAssert.Contains(executionRead, "FROM dbo.InventoryLedgerEntry e JOIN RelatedFacts r");
    }

    /// <summary>四类入库仓储均按请求注册，并复用同一请求级 RepositoryBaseContext 注入边界。</summary>
    [TestMethod]
    public void Repositories_UseScopedSharedContext()
    {
        var services = new ServiceCollection().AddInfrastructureServices();
        Assert.AreEqual(ServiceLifetime.Scoped, services.Single(x => x.ServiceType == typeof(RepositoryBaseContext)).Lifetime);
        var repositories = new (Type Interface, Type Implementation)[]
        {
            (typeof(IInboundQueryRepository), typeof(PostgreSqlInboundQueryRepository)),
            (typeof(IInboundSourceRepository), typeof(PostgreSqlInboundSourceRepository)),
            (typeof(IInboundDocumentRepository), typeof(PostgreSqlInboundDocumentRepository)),
            (typeof(IInboundExecutionReadRepository), typeof(PostgreSqlInboundExecutionReadRepository))
        };
        foreach (var repository in repositories)
        {
            var registration = services.Single(x => x.ServiceType == repository.Interface);
            Assert.AreEqual(ServiceLifetime.Scoped, registration.Lifetime);
            Assert.AreEqual(repository.Implementation, registration.ImplementationType);
            Assert.AreEqual(typeof(RepositoryBaseContext), repository.Implementation.GetConstructors().Single().GetParameters().Single().ParameterType);
        }
        Assert.IsFalse(services.Any(x => x.ServiceType.Name == "IInboundManagerRepository"));
    }

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (Directory.Exists(Path.Combine(dir.FullName, "SwiftExport.Application"))) return dir.FullName;
        throw new DirectoryNotFoundException("未找到测试源码根目录。");
    }
}

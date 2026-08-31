using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Services.DemandPlanning;
using SwiftExport.Application.Services.Procurement;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrders;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Core.Entities.Procurement;
using SwiftExport.Core.Interfaces.Procurement;
using System.Reflection;

namespace SwiftExport.Application.Tests.Procurement;

/// <summary>验证旧采购查询和生命周期入口不能接管新执行链，并保留旧订单正常读取。</summary>
[TestClass]
public sealed class LegacyExecutionIsolationTests
{
    /// <summary>即使仓储误返回新链采购头，旧详情、追溯和独立报告入口都不得继续读取附属事实。</summary>
    [TestMethod]
    public async Task LegacyQueries_RejectNewExecutionBeforeReadingAssociatedFacts()
    {
        var head = new PurchaseOrderHead { Uid = Guid.NewGuid(), ExecutionModel = 1 };
        var unexpectedCalls = 0;
        var repository = Proxy<IPurchaseOrderWorkspaceRepository>((method, _) => method.Name switch
        {
            "GetHeadAsync" => Task.FromResult<PurchaseOrderHead?>(head),
            "GetHeadSummaryAsync" => Task.FromResult<PurchaseOrderListItemDto?>(new() { Uid = head.Uid }),
            _ => UnexpectedCall(method)
        });
        var reports = Proxy<ISupplierProgressReportRepository>((method, _) =>
            UnexpectedCall(method));
        var service = new PurchaseOrderWorkspaceService(Context(), repository, reports, null!, null!, null!);

        Assert.IsFalse((await service.GetDetailAsync(head.Uid)).Success);
        Assert.IsFalse((await service.GetTraceAsync(head.Uid)).Success);
        Assert.IsFalse((await service.GetProgressAsync(head.Uid)).Success);
        Assert.AreEqual(0, unexpectedCalls);

        object? UnexpectedCall(MethodInfo method)
        {
            unexpectedCalls++;
            throw new AssertFailedException($"不应读取新链附属事实：{method.Name}");
        }
    }

    /// <summary>旧链和来源未知历史仍可读取反馈，不因新增所有权隔离要求补造需求来源。</summary>
    [TestMethod]
    public async Task LegacyProgress_PreservesExistingOrderReadWithoutDemandSource()
    {
        var head = new PurchaseOrderHead { Uid = Guid.NewGuid(), ExecutionModel = 0 };
        var report = new SupplierProgressReportDto { Uid = Guid.NewGuid(), ReadyQty = 2m };
        var calls = 0;
        var repository = Proxy<IPurchaseOrderWorkspaceRepository>((method, _) => method.Name switch
        {
            "GetHeadAsync" => Task.FromResult<PurchaseOrderHead?>(head),
            _ => throw new AssertFailedException($"非预期仓储调用：{method.Name}")
        });
        var reports = Proxy<ISupplierProgressReportRepository>((method, _) =>
        {
            Assert.AreEqual("GetByPurchaseOrderAsync", method.Name);
            calls++;
            return Task.FromResult<IReadOnlyList<SupplierProgressReportDto>>(new[] { report });
        });
        var service = new PurchaseOrderWorkspaceService(Context(), repository, reports, null!, null!, null!);

        var result = await service.GetProgressAsync(head.Uid);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(1, calls);
        Assert.AreEqual(report.Uid, result.Data!.Single().Uid);
    }

    /// <summary>新链订单即使伪装为完整旧需求来源且状态可变，也不能进入旧减量预览。</summary>
    [TestMethod]
    public async Task LegacyReleasePreview_RejectsNewExecutionWithCompleteDemandIdentity()
    {
        var head = new PurchaseOrderHead
        {
            Uid = Guid.NewGuid(), Version = 1, ExecutionModel = 1,
            SupplierId = Guid.NewGuid(), DemandSourceUid = Guid.NewGuid(),
            DemandSourceType = "SO", DemandSourceNo = "SO-TEST", ExecutionRequestId = Guid.NewGuid(),
            LifecycleStatus = PurchaseOrderLifecycleStatusEnum.Active,
            ConfirmationStatus = ConfirmationStatusEnum.待确认
        };
        var repository = Proxy<IPurchaseOrderDraftRepository>((method, _) => method.Name switch
        {
            "GetLifecycleDraftAsync" => Task.FromResult<PurchaseOrderHead?>(head),
            _ => throw new AssertFailedException($"不应读取新链可释放数量：{method.Name}")
        });
        var service = new PurchaseDraftLifecycleService(Context(), repository, null!, null!);

        var result = await service.PreviewReleaseAsync(head.Uid,
            new PurchaseDraftReleasePreviewRequest { ExpectedVersion = 1, NewTotalQty = 1m, Reason = "验证执行链隔离" });

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "EXECUTION_MODEL_MISMATCH");
    }

    /// <summary>SQL叶子写入必须独立限制执行链，不能只依赖UI或前一次读取的来源判定。</summary>
    [TestMethod]
    public void LegacyRepositories_KeepExecutionOwnershipOnLeafWritesAndHeadProjection()
    {
        var workspace = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderWorkspaceRepository.cs");
        var drafts = Read("SwiftExport.Infrastructure", "Repositories", "Procurement", "PurchaseOrderDraftRepository.cs");
        StringAssert.Contains(workspace, "h.Uid,h.ExecutionModel,h.Version");
        StringAssert.Contains(drafts, "h.Uid,h.ExecutionModel,h.Version");
        StringAssert.Contains(workspace, "WHERE Uid=@Uid AND ExecutionModel=0 AND Version=@ExpectedVersion");
        StringAssert.Contains(drafts, "WHERE Uid=@Uid AND ExecutionModel=0 AND Version=@ExpectedVersion");
        StringAssert.Contains(drafts, "owner.Uid=dbo.PurchaseOrderLine.PurchaseOrderId AND owner.ExecutionModel=0");
        StringAssert.Contains(drafts, "旧采购仓储不能向新执行链订单追加采购行");
    }

    // 本组只读服务测试不使用连接；禁止为隔离测试连接真实业务库。
    private static ServiceBaseContext Context() => new(null!, NullLoggerFactory.Instance);

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, RepositoryProxy>();
        ((RepositoryProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    /// <summary>测试专用仓储代理；仅允许测试显式声明的调用，其他调用立即失败。</summary>
    public class RepositoryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;

        /// <summary>把仓储调用交给场景处理器，验证所有权失败前未访问附属业务事实。</summary>
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => Handler(targetMethod!, args);
    }

    private static string Read(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return File.ReadAllText(Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray()));
        throw new AssertFailedException("无法定位仓库根目录。");
    }
}

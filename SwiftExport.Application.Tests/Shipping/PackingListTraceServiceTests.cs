using Mapster;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using SwiftExport.WebApi.Controllers.Shipping;
using Microsoft.AspNetCore.Mvc;
using System.Reflection;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证追踪只返回原证据、拒绝跨单据事实，并保持查询路由与权限边界。</summary>
[TestClass]
public sealed class PackingListTraceServiceTests
{
    /// <summary>正反向证据保留各自流水及原行身份，不泄露内部日志载荷。</summary>
    [TestMethod]
    public async Task PreservesPhysicalEvidenceAndRejectsForeignRows()
    {
        var id = Guid.NewGuid();
        var execution = new PackingListPostingExecution { PackingListHeadId = id, RequestFingerprint = "secret", ResultJson = "private" };
        var row = new PackingListPostingExecutionLine { ExecutionId = execution.Uid, PackingListLineId = Guid.NewGuid(),
            GrossDemandId = Guid.NewGuid(), LedgerId = Guid.NewGuid(), Qty = 1.2345m, DocumentNo = "OUT1" };
        var result = await Create(id, execution, row).GetAsync(id, default);
        Assert.IsTrue(result.Success);
        Assert.AreEqual(row.LedgerId, result.Data.Executions[0].Lines[0].LedgerId);
        Assert.AreEqual(row.PackingListLineId, result.Data.Executions[0].Lines[0].PackingListLineId);
        Assert.AreEqual(1.2345m, result.Data.Executions[0].Lines[0].Qty);
        Assert.IsNull(typeof(PackingListExecutionTraceDto).GetProperty("RequestFingerprint"));
        Assert.IsNull(typeof(PackingListExecutionTraceDto).GetProperty("ResultJson"));
        row.ExecutionId = Guid.NewGuid();
        Assert.AreEqual("EXECUTION_INVALID", (await Create(id, execution, row).GetAsync(id, default)).Error.Code);
    }

    /// <summary>权限拒绝时不查询数据库；模块访问不自动授予过账及反向能力。</summary>
    [TestMethod]
    public async Task PermissionIsIndependentAndFailsClosed()
    {
        var id = Guid.NewGuid();
        var result = await Create(id, new(), new(), false).GetAsync(id, default);
        Assert.AreEqual("FORBIDDEN", result.Error.Code);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.PackingListPost);
        CollectionAssert.Contains(PermissionKeys.UserAssignable.ToList(), PermissionKeys.PackingListReverse);
        CollectionAssert.DoesNotContain(PermissionKeys.Modules.ToList(), PermissionKeys.PackingListPost);
        CollectionAssert.DoesNotContain(PermissionKeys.ExtractPermissions([PermissionKeys.ModuleShippingAccess]), PermissionKeys.PackingListReverse);
    }

    /// <summary>追踪 Controller 与 Refit 使用相同 GET 路径和取消参数。</summary>
    [TestMethod]
    public void TraceRouteMatches()
    {
        var api = typeof(IPackingListApi).GetMethod(nameof(IPackingListApi.TraceAsync))!;
        var controller = typeof(PackingListController).GetMethod(nameof(PackingListController.TraceAsync))!;
        Assert.AreEqual("/{uid}/trace", api.GetCustomAttribute<GetAttribute>()!.Path);
        Assert.AreEqual("{uid:guid}/trace", controller.GetCustomAttribute<HttpGetAttribute>()!.Template);
        Assert.AreEqual(typeof(CancellationToken), api.GetParameters().Last().ParameterType);
        Assert.AreEqual(typeof(CancellationToken), controller.GetParameters().Last().ParameterType);
    }

    /// <summary>严格只读夹具，未授权时任何仓储调用直接失败。</summary>
    private static PackingListTraceService Create(Guid id, PackingListPostingExecution execution,
        PackingListPostingExecutionLine row, bool allowed = true)
    {
        var user = Proxy.Make<ICurrentUser>((m, _) => m switch { "get_IsAuthenticated" => true, "get_UserId" => id, _ => throw new NotSupportedException(m) });
        var permission = Proxy.Make<IUserPermissionService>((m, _) => Task.FromResult(allowed));
        var packing = Proxy.Make<IPackingListQueryRepository>((m, _) => allowed && m == "GetDetailAsync"
            ? Task.FromResult<PackingListDetailDto?>(new() { Header = new() { Uid = id, PackingListNo = "PL1" } }) : throw new AssertFailedException(m));
        var repository = Proxy.Make<IPackingListExecutionRepository>((m, _) => !allowed ? throw new AssertFailedException(m) : m switch
        {
            "IsSchemaReadyAsync" => Task.FromResult(true),
            "ReadAsync" => Task.FromResult(((IReadOnlyList<PackingListPostingExecution>)new[] { execution },
                (IReadOnlyList<PackingListPostingExecutionLine>)new[] { row })),
            _ => throw new AssertFailedException(m)
        });
        return new(packing, repository, user, permission, new TypeAdapterConfig());
    }
}

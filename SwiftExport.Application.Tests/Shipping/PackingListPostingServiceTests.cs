using System.Reflection;
using System.Runtime.CompilerServices;
using Mapster;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Services.Shipping.PackingLists;
using SwiftExport.Application.Mappings;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.WebApi.Controllers.Shipping;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using Proxy = SwiftExport.Application.Tests.Shipping.PackingListPostingCoordinatorTests.Proxy;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证过账 HTTP 用例的契约、稳定失败信息及不可变成功重放。</summary>
[TestClass]
public sealed class PackingListPostingServiceTests
{
    /// <summary>四个业务路由保持方法、参数来源与取消一致。</summary>
    [TestMethod]
    [DataRow("PostPreviewAsync", "post-preview")]
    [DataRow("PostAsync", "post")]
    [DataRow("ReversePreviewAsync", "reverse-preview")]
    [DataRow("ReverseAsync", "reverse")]
    public void RoutesMatch(string method, string route)
    {
        var api = typeof(IPackingListApi).GetMethod(method)!;
        var controller = typeof(PackingListController).GetMethod(method)!;
        Assert.AreEqual("/{uid}/" + route, api.GetCustomAttribute<PostAttribute>()!.Path);
        Assert.AreEqual("{uid:guid}/" + route, controller.GetCustomAttribute<HttpPostAttribute>()!.Template);
        Assert.IsNotNull(api.GetParameters()[1].GetCustomAttribute<BodyAttribute>());
        Assert.IsNotNull(controller.GetParameters()[1].GetCustomAttribute<FromBodyAttribute>());
        Assert.AreEqual(typeof(CancellationToken), api.GetParameters()[2].ParameterType);
        Assert.AreEqual(typeof(CancellationToken), controller.GetParameters()[2].ParameterType);
    }

    /// <summary>正式命令失败使用 Error，失败 Data 为空；逐来源阻断由预览契约提供。</summary>
    [TestMethod]
    public async Task FailureUsesErrorAndNullData()
    {
        var source = Guid.NewGuid();
        var failure = new PackingListPostingFailure("SUPPLY_SHORTFALL", "库存不足")
        { Blockers = [new("SUPPLY_SHORTFALL", source, "库存不足", 2), new("SOURCE_INVALID", Guid.NewGuid(), "来源失效")] };
        var result = await Execute(() => Task.FromException<PackingListPostOutcome>(failure));
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Data);
        Assert.AreEqual("SUPPLY_SHORTFALL", result.Error.Code);
        Assert.AreEqual("库存不足", result.Error.Message);
        Assert.AreEqual("PREVIEW_STALE", (await Execute(() => Task.FromException<PackingListPostOutcome>(
            new PackingListPostingFailure("PREVIEW_STALE", "重新预览")))).Error.Code);
    }

    /// <summary>命令只转交用户确认字段，成功重放返回原单据版本而不再读取仓储。</summary>
    [TestMethod]
    public async Task ReplayPreservesActualDocuments()
    {
        var f = new PackingListPostingCoordinatorTests.Fixture(inventory: true);
        var preview = await f.Service.PreviewCoreAsync(f.Head.Uid, 1, f.Date, default);
        var command = new PostPackingListCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 1,
            BusinessDate = f.Date, PreviewToken = preview.Token! };
        var posted = await f.Service.PostAsync(f.Head.Uid, command, default);
        var replay = await f.Service.PostAsync(f.Head.Uid, command, default);
        Assert.IsTrue(posted.Success);
        Assert.IsTrue(replay.Success);
        Assert.AreEqual("AlreadyApplied", replay.Data.Status);
        Assert.AreEqual(posted.Data.ExecutionId, replay.Data.ExecutionId);
        CollectionAssert.AreEqual(posted.Data.Documents.Select(x => x.Uid).ToArray(), replay.Data.Documents.Select(x => x.Uid).ToArray());
        Assert.IsTrue(replay.Data.Documents.Count > 0);
        var document = new PackingListGeneratedDocumentDto { Uid = Guid.NewGuid(), DocumentNo = "IN1", Version = 2, Direction = "Inbound" };
        var result = await Execute(() => Task.FromResult(new PackingListPostOutcome { AlreadyApplied = true, Documents = [document] }));
        Assert.IsTrue(result.Success); Assert.AreEqual("AlreadyApplied", result.Data.Status);
        Assert.AreEqual("IN1", result.Data.Documents[0].DocumentNo); Assert.AreEqual(2, result.Data.Documents[0].Version);
    }

    /// <summary>内部动作字段映射保持计算依据及四位数量，空展示字段不猜测。</summary>
    [TestMethod]
    public void ActionMappingPreservesCalculation()
    {
        var config = new TypeAdapterConfig(); new PackingListPostingMapping().Register(config);
        var source = new PackingListPostingPreviewBuilder.Action("Consumption", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "PCS",
            Guid.NewGuid(), null, Guid.NewGuid(), Guid.NewGuid(), 10, 3, 4, 1.2m);
        var dto = source.Adapt<PackingListPostingActionDto>(config);
        Assert.AreEqual(source.WarehouseId, dto.WarehouseSiteId); Assert.AreEqual(source.ComponentId, dto.ComponentNetDemandId);
        Assert.AreEqual(1.2m, dto.Quantity); Assert.AreEqual("Consumption", dto.ActionKind); Assert.IsNull(dto.ImagePath);
    }

    /// <summary>单独验证完成结果映射；公共入口的授权、回执和业务执行由真实夹具覆盖。</summary>
    private static Task<SwiftExport.Contracts.Wrappers.ServiceResult<PackingListPostingCommandResult>> Execute(
        Func<Task<PackingListPostOutcome>> action)
    {
        var service = (PackingListPostingService)RuntimeHelpers.GetUninitializedObject(typeof(PackingListPostingService));
        var method = typeof(PackingListPostingService).GetMethod("Execute", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task<SwiftExport.Contracts.Wrappers.ServiceResult<PackingListPostingCommandResult>>)method.Invoke(service, [action])!;
    }
}

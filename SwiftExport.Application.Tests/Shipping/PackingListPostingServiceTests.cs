using System.Reflection;
using Mapster;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Refit;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Mappings;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.Apis.Shipping;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.WebApi.Controllers.Shipping;
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

    /// <summary>多个来源阻断不丢失数量定位，预览失效分类稳定。</summary>
    [TestMethod]
    public async Task KeepsAllBlockersAndStableStatus()
    {
        var source = Guid.NewGuid();
        var failure = new PackingListPostingFailure("SUPPLY_SHORTFALL", "库存不足")
        { Blockers = [new("SUPPLY_SHORTFALL", source, "库存不足", 2), new("SOURCE_INVALID", Guid.NewGuid(), "来源失效")] };
        var service = Create((_, _) => Task.FromException<PackingListPostOutcome>(failure));
        var result = await service.PostAsync(Guid.NewGuid(), new(), default);
        Assert.IsFalse(result.Success);
        Assert.AreEqual("Blocked", result.Data.Status);
        Assert.AreEqual(2, result.Data.Blockers.Count);
        Assert.AreEqual(source, result.Data.Blockers[0].GrossDemandId);
        Assert.AreEqual(2m, result.Data.Blockers[0].ShortfallQty);
        service = Create((_, _) => Task.FromException<PackingListPostOutcome>(new PackingListPostingFailure("PREVIEW_STALE", "重新预览")));
        Assert.AreEqual("PreviewExpired", (await service.PostAsync(Guid.NewGuid(), new(), default)).Data.Status);
    }

    /// <summary>命令只转交用户确认字段，成功重放返回原单据版本而不再读取仓储。</summary>
    [TestMethod]
    public async Task ReplayPreservesActualDocuments()
    {
        var uid = Guid.NewGuid(); var command = new PostPackingListCommand { CommandId = Guid.NewGuid(), ExpectedVersion = 4,
            BusinessDate = new(2026, 9, 17), PreviewToken = "token" };
        var document = new PackingListGeneratedDocumentDto { Uid = Guid.NewGuid(), DocumentNo = "IN1", Version = 2, Direction = "Inbound" };
        var service = Create((name, args) =>
        {
            var request = (PackingListPostRequest)args[0]!;
            Assert.AreEqual(uid, request.PackingListId); Assert.AreEqual(command.CommandId, request.CommandId);
            Assert.AreEqual(command.PreviewToken, request.PreviewToken);
            return Task.FromResult(new PackingListPostOutcome { AlreadyApplied = true, Documents = [document] });
        });
        var result = await service.PostAsync(uid, command, default);
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

    /// <summary>仅执行协调器转发，仓储传空以检测命令完成后是否意外读取数据库。</summary>
    private static PackingListPostingService Create(Func<string, object?[], object?> handler)
    {
        var config = new TypeAdapterConfig(); new PackingListPostingMapping().Register(config);
        return new(Proxy.Make<IPackingListPostingCoordinator>(handler), null!, null!, null!, config);
    }
}

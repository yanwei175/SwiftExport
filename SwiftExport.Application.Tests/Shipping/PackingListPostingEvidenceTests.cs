using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Interfaces.Inventory;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Services.Shipping;

namespace SwiftExport.Application.Tests.Shipping;

/// <summary>验证正式端口返回值与箱单执行证据的映射，缺失流水时必须失败。</summary>
[TestClass]
public sealed class PackingListPostingEvidenceTests
{
    /// <summary>记录真实单据行和流水，不以来源或采购身份替代。</summary>
    [TestMethod]
    public void MapsActualInventoryResults()
    {
        var f = new Fixture();
        var evidence = SwiftExport.Application.Services.Shipping.PackingLists.PackingListPostingService.CreateEvidence(f.ExecutionId, f.Preview, f.Commands, new(), f.Result);
        var row = evidence.Single();
        Assert.AreEqual(f.Action.PackingListLineId, row.PackingListLineId);
        Assert.AreEqual(f.Document.DocumentId, row.DocumentId);
        Assert.AreEqual(f.Document.Lines.Single().DocumentLineId, row.DocumentLineId);
        Assert.AreEqual(f.Document.Lines.Single().LedgerId, row.LedgerId);
        Assert.AreEqual(f.Action.GrossDemandId, row.GrossDemandId);
        Assert.AreEqual(f.Action.Qty, row.Qty);
        Assert.AreEqual(f.ExecutionId, row.ExecutionId);
    }

    /// <summary>缺行、重复行或空流水不能持久化为成功执行。</summary>
    [TestMethod]
    public void RejectsIncompleteInventoryResults()
    {
        var f = new Fixture();
        Assert.ThrowsExactly<PackingListPostingFailure>(() => SwiftExport.Application.Services.Shipping.PackingLists.PackingListPostingService.CreateEvidence(
            f.ExecutionId, f.Preview, f.Commands, new(), new()));
        f.Document.Lines.Add(f.Document.Lines[0]);
        Assert.ThrowsExactly<PackingListPostingFailure>(() => SwiftExport.Application.Services.Shipping.PackingLists.PackingListPostingService.CreateEvidence(
            f.ExecutionId, f.Preview, f.Commands, new(), f.Result));
        f.Document.Lines.RemoveAt(1);
        f.Document.Lines[0] = f.Document.Lines[0] with { LedgerId = Guid.Empty };
        Assert.ThrowsExactly<PackingListPostingFailure>(() => SwiftExport.Application.Services.Shipping.PackingLists.PackingListPostingService.CreateEvidence(
            f.ExecutionId, f.Preview, f.Commands, new(), f.Result));
    }

    /// <summary>仅登记交付的售后也有执行证据，但不伪造单据和流水。</summary>
    [TestMethod]
    public void DeliveryEvidenceHasNoInventoryIdentity()
    {
        var f = new Fixture();
        var preview = f.Preview with { Actions = [f.Action with { Kind = "Delivery", WarehouseId = null }] };
        var commands = new PackingListAutomaticCommandBuilder.Commands(null, null, null,
            new Dictionary<Guid, PackingListPostingPreviewBuilder.Action>());
        var row = SwiftExport.Application.Services.Shipping.PackingLists.PackingListPostingService.CreateEvidence(f.ExecutionId, preview, commands, new(), new()).Single();
        Assert.AreEqual("Delivery", row.ActionKind);
        Assert.IsNull(row.DocumentId);
        Assert.IsNull(row.LedgerId);
        Assert.AreEqual(f.Action.Qty, row.Qty);
    }

    /// <summary>固定正向动作及返回值，不启动业务事务。</summary>
    private sealed class Fixture
    {
        public Guid ExecutionId { get; } = Guid.NewGuid();
        public PackingListPostingPreviewBuilder.Action Action { get; } = new("Shipment", Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), "PCS", Guid.NewGuid(), null, Guid.NewGuid(), null, 100, 0, 10, 10);
        public PackingListPostingPreviewBuilder.Preview Preview { get; }
        public PackingListAutomaticCommandBuilder.Commands Commands { get; }
        public AutomaticPostingResult Result { get; }
        public AutomaticDocumentResult Document { get; }
        public Fixture()
        {
            Preview = new("token", [Action], []);
            Commands = new PackingListAutomaticCommandBuilder().Build(Guid.NewGuid(), Guid.NewGuid(), "PL", DateTime.Today,
                Preview, [new(Action.WarehouseId!.Value, Action.ProductId!.Value)], []);
            var requestLine = Commands.LineActions.Keys.Single();
            Document = new AutomaticDocumentResult { DocumentId = Guid.NewGuid(), DocumentNo = "OUT1", Version = 2,
                Lines = [new(requestLine, Guid.NewGuid(), Guid.NewGuid(), requestLine)] };
            Result = new AutomaticPostingResult { Documents = [Document] };
        }
    }
}

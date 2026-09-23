using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Inventory.Validation.Inbounds;
using SwiftExport.Application.Services.Inventory.Validation.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Inbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Commands;

/// <summary>覆盖从草稿服务迁出的代表性输入边界，避免请求验证误触数据库状态。</summary>
[TestClass]
public sealed class DraftCommandValidatorTests
{
    /// <summary>入库数量错误应保留明细索引路径，重复来源和创建已有行也应拒绝。</summary>
    [TestMethod]
    public async Task Inbound_QuantityDuplicateAndExistingLine_AreRejected()
    {
        var command = new CreateInboundDraftCommand
        {
            CommandId = Guid.NewGuid(),
            Draft = new InboundDraftData
            {
                DocumentType = InboundDocumentTypeEnum.PurchaseReceipt,
                WarehouseSiteId = Guid.NewGuid(), ReceiptDate = DateTime.Today,
                Lines = [new() { SourceLineId = Guid.NewGuid(), Quantity = 1.00001m }]
            }
        };
        var validator = new CreateInboundDraftCommandValidator();
        var invalid = await validator.ValidateAsync(command);
        Assert.AreEqual("Draft.Lines[0].Quantity", invalid.Errors[0].PropertyName);
        command.Draft.Lines[0].Quantity = 1;
        command.Draft.Lines.Add(new InboundDraftLineInput { SourceLineId = command.Draft.Lines[0].SourceLineId, Quantity = 1 });
        Assert.IsTrue((await validator.ValidateAsync(command)).Errors.Any(x => x.PropertyName == "Draft.Lines"));
        command.Draft.Lines.RemoveAt(1);
        command.Draft.Lines[0].Uid = Guid.NewGuid();
        Assert.IsTrue((await validator.ValidateAsync(command)).Errors.Any(x => x.PropertyName == "Draft.Lines"));
    }

    /// <summary>出库允许空草稿行，拒绝空行对象与超出精度的数量。</summary>
    [TestMethod]
    public async Task Outbound_EmptyDraftIsAllowed_NullLineIsRejected()
    {
        var command = new CreateOutboundDraftCommand
        {
            CommandId = Guid.NewGuid(),
            Draft = new OutboundDraftData
            {
                DocumentType = OutboundDocumentTypeEnum.StockIssue,
                WarehouseSiteId = Guid.NewGuid(), ShipmentDate = DateTime.Today
            }
        };
        var validator = new CreateOutboundDraftCommandValidator();
        Assert.IsTrue((await validator.ValidateAsync(command)).IsValid);
        command.Draft.Lines.Add(null!);
        var invalid = await validator.ValidateAsync(command);
        Assert.IsTrue(invalid.Errors.Any(x => x.PropertyName == "Draft.Lines[0]"));
        command.Draft.Lines[0] = new OutboundDraftLineInput { ProductId = Guid.NewGuid(), Quantity = 0.00001m };
        invalid = await validator.ValidateAsync(command);
        Assert.IsTrue(invalid.Errors.Any(x => x.PropertyName == "Draft.Lines[0].Quantity"));
    }

    /// <summary>更新只要求请求版本为正数，旧成功版本重放由执行器判定。</summary>
    [TestMethod]
    public async Task Update_OnlyRequiresPositiveVersion()
    {
        var command = new UpdateOutboundDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1,
            Draft = new OutboundDraftData
            {
                DocumentType = OutboundDocumentTypeEnum.StockIssue,
                WarehouseSiteId = Guid.NewGuid(), ShipmentDate = DateTime.Today
            }
        };
        var validator = new UpdateOutboundDraftCommandValidator();
        Assert.IsTrue((await validator.ValidateAsync(command)).IsValid);
        command.ExpectedVersion = 0;
        Assert.IsTrue((await validator.ValidateAsync(command)).Errors.Any(x => x.PropertyName == "ExpectedVersion"));
    }
}

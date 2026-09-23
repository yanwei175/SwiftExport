using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using SwiftExport.Application.Services.Inventory.Inbounds.Validation;
using SwiftExport.Application.Services.Inventory.Validation.Outbounds;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Tests.Commands;

/// <summary>覆盖从草稿服务迁出的代表性输入边界，避免请求验证误触数据库状态。</summary>
[TestClass]
public sealed class DraftCommandValidatorTests
{
    /// <summary>程序集扫描应为六个已接入命令各注册一个可解析的 Validator。</summary>
    [TestMethod]
    public void ApplicationRegistration_ResolvesSixValidatorsOnce()
    {
        var registrations = new ServiceCollection();
        registrations.AddApplicationServices();
        using var provider = registrations.BuildServiceProvider();
        AssertOneValidator<CreateInboundDraftCommand, CreateInboundDraftCommandValidator>(registrations, provider);
        AssertOneValidator<UpdateInboundDraftCommand, UpdateInboundDraftCommandValidator>(registrations, provider);
        AssertOneValidator<DeleteInboundDraftCommand, DeleteInboundDraftCommandValidator>(registrations, provider);
        AssertOneValidator<CreateOutboundDraftCommand, CreateOutboundDraftCommandValidator>(registrations, provider);
        AssertOneValidator<UpdateOutboundDraftCommand, UpdateOutboundDraftCommandValidator>(registrations, provider);
        AssertOneValidator<DeleteOutboundDraftCommand, DeleteOutboundDraftCommandValidator>(registrations, provider);
    }

    /// <summary>空删除原因和空明细集合应返回中文消息与稳定字段路径。</summary>
    [TestMethod]
    public async Task EmptyReasonAndNullLines_UseChineseFieldErrors()
    {
        var inboundReason = await new DeleteInboundDraftCommandValidator().ValidateAsync(new DeleteInboundDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = ""
        });
        var outboundReason = await new DeleteOutboundDraftCommandValidator().ValidateAsync(new DeleteOutboundDraftCommand
        {
            CommandId = Guid.NewGuid(), ExpectedVersion = 1, Reason = ""
        });
        AssertError(inboundReason.Errors, "Reason", "请填写删除原因。");
        AssertError(outboundReason.Errors, "Reason", "请填写删除原因。");

        var inboundLines = await new CreateInboundDraftCommandValidator().ValidateAsync(new CreateInboundDraftCommand
        {
            CommandId = Guid.NewGuid(), Draft = new InboundDraftData
            {
                DocumentType = InboundDocumentTypeEnum.PurchaseReceipt, WarehouseSiteId = Guid.NewGuid(),
                ReceiptDate = DateTime.Today, Lines = null!
            }
        });
        var outboundLines = await new CreateOutboundDraftCommandValidator().ValidateAsync(new CreateOutboundDraftCommand
        {
            CommandId = Guid.NewGuid(), Draft = new OutboundDraftData
            {
                DocumentType = OutboundDocumentTypeEnum.StockIssue, WarehouseSiteId = Guid.NewGuid(),
                ShipmentDate = DateTime.Today, Lines = null!
            }
        });
        AssertError(inboundLines.Errors, "Draft.Lines", "入库草稿明细不能为空。");
        AssertError(outboundLines.Errors, "Draft.Lines", "出库草稿明细不能为空。");
    }

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

    /// <summary>确认扫描注册的数量和实际实现类型，防止叠加显式注册。</summary>
    private static void AssertOneValidator<TRequest, TValidator>(IServiceCollection registrations, IServiceProvider provider)
        where TValidator : IValidator<TRequest>
    {
        Assert.AreEqual(1, registrations.Count(x => x.ServiceType == typeof(IValidator<TRequest>)));
        Assert.IsInstanceOfType<TValidator>(provider.GetRequiredService<IValidator<TRequest>>());
    }

    /// <summary>确认对外首条字段错误使用指定中文消息。</summary>
    private static void AssertError(IEnumerable<FluentValidation.Results.ValidationFailure> errors, string field, string message)
    {
        var first = errors.First(x => x.PropertyName == field);
        Assert.AreEqual(message, first.ErrorMessage);
    }
}

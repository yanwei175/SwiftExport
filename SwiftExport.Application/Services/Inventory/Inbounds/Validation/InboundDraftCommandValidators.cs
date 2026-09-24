using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Draft;
using FluentValidation;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Services.Inventory.Inbounds.Validation;

/// <summary>验证入库草稿请求结构，不查询单据状态或来源可用量。</summary>
public sealed class InboundDraftDataValidator : AbstractValidator<InboundDraftData>
{
    /// <summary>限制人工采购入库、明细身份与数量格式。</summary>
    public InboundDraftDataValidator()
    {
        RuleFor(x => x.DocumentType).Equal(InboundDocumentTypeEnum.PurchaseReceipt).WithMessage("当前只支持采购入库人工草稿。");
        RuleFor(x => x.WarehouseSiteId).NotEmpty().WithMessage("请选择收货仓库。");
        RuleFor(x => x.ReceiptDate).NotEqual(default(DateTime)).WithMessage("请选择收货日期。");
        RuleFor(x => x.Remark).MaximumLength(500).WithMessage("备注最多 500 字。");
        RuleFor(x => x.Lines).NotNull().WithMessage("入库草稿明细不能为空。")
            .Must(x => x is { Count: >= 1 and <= 500 }).WithMessage("每张草稿须包含 1–500 行。");
        RuleForEach(x => x.Lines).NotNull().WithMessage("入库行不能为空。");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.SourceLineId).NotEmpty().WithMessage("来源不能为空。");
            line.RuleFor(x => x.Quantity).Must(ValidQuantity).WithMessage("实收数量须为正数、最多四位小数且符合 decimal(18,4)。");
        });
        RuleFor(x => x.Lines).Must(x => x is null || x.Any(y => y is null) ||
            x.Select(y => y.SourceLineId).Distinct().Count() == x.Count &&
            x.Where(y => y.Uid != Guid.Empty).Select(y => y.Uid).Distinct().Count() == x.Count(y => y.Uid != Guid.Empty))
            .WithMessage("同一张入库单不能重复来源或明细标识。");
    }

    /// <summary>与数据库 decimal(18,4) 的正数边界保持一致。</summary>
    private static bool ValidQuantity(decimal value) => value > 0 && value <= 99999999999999.9999m && decimal.Round(value, 4) == value;
}

/// <summary>验证创建入库草稿的命令身份和新增行约束。</summary>
public sealed class CreateInboundDraftCommandValidator : AbstractValidator<CreateInboundDraftCommand>
{
    /// <summary>校验 CommandId 与草稿输入，新增行不得携带已有身份。</summary>
    public CreateInboundDraftCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.Draft).NotNull().WithMessage("草稿不能为空。").SetValidator(new InboundDraftDataValidator()!);
        RuleFor(x => x.Draft.Lines).Must(x => x is null || x.All(y => y is null || y.Uid == Guid.Empty))
            .When(x => x.Draft is not null).WithMessage("新增草稿行不能携带已有入库行标识。");
    }
}

/// <summary>验证更新入库草稿的命令身份与请求版本格式。</summary>
public sealed class UpdateInboundDraftCommandValidator : AbstractValidator<UpdateInboundDraftCommand>
{
    /// <summary>只要求版本为正数，当前数据库版本留在事务内核对。</summary>
    public UpdateInboundDraftCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
        RuleFor(x => x.Draft).NotNull().WithMessage("草稿不能为空。").SetValidator(new InboundDraftDataValidator()!);
    }
}

/// <summary>验证删除入库草稿的命令身份、版本格式和原因。</summary>
public sealed class DeleteInboundDraftCommandValidator : AbstractValidator<DeleteInboundDraftCommand>
{
    /// <summary>删除原因限制在 1–500 字，单据状态仍由事务内业务校验。</summary>
    public DeleteInboundDraftCommandValidator()
    {
        RuleFor(x => x.CommandId)
            .NotEmpty()
            .WithMessage("CommandId 不能为空。");

        RuleFor(x => x.ExpectedVersion)
            .GreaterThan(0)
            .WithMessage("ExpectedVersion 必须为正数。");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("请填写删除原因。")
            .MaximumLength(500).WithMessage("请填写不超过 500 字的删除原因。");
    }
}

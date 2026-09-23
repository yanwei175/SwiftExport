using FluentValidation;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;
using SwiftExport.Contracts.SysEnums.Inventory;

namespace SwiftExport.Application.Services.Inventory.Validation.Outbounds;

/// <summary>验证出库草稿请求结构，不提前核查库存或产品当前状态。</summary>
public sealed class OutboundDraftDataValidator : AbstractValidator<OutboundDraftData>
{
    /// <summary>限制普通库存出库、明细身份与数量格式；允许空明细草稿。</summary>
    public OutboundDraftDataValidator()
    {
        RuleFor(x => x.DocumentType).Equal(OutboundDocumentTypeEnum.StockIssue).WithMessage("当前只支持普通库存出库人工草稿。");
        RuleFor(x => x.WarehouseSiteId).NotEmpty().WithMessage("请选择出库仓库。");
        RuleFor(x => x.ShipmentDate).NotEqual(default(DateTime)).WithMessage("请选择出库日期。");
        RuleFor(x => x.Remark).MaximumLength(500).WithMessage("备注最多 500 字。");
        RuleFor(x => x.IssueReason).MaximumLength(500).WithMessage("出库原因最多 500 字。");
        RuleFor(x => x.Lines).NotNull().WithMessage("出库草稿明细不能为空。")
            .Must(x => x is { Count: <= 500 }).WithMessage("每张草稿最多包含 500 行。");
        RuleForEach(x => x.Lines).NotNull().WithMessage("出库行不能为空。");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(x => x.ProductId).NotEmpty().WithMessage("产品不能为空。");
            line.RuleFor(x => x.Quantity).Must(ValidQuantity).WithMessage("出库数量须为正数、最多四位小数且符合 decimal(18,4)。");
        });
        RuleFor(x => x.Lines).Must(x => x is null || x.Any(y => y is null) ||
            x.Select(y => y.ProductId).Distinct().Count() == x.Count &&
            x.Where(y => y.Uid != Guid.Empty).Select(y => y.Uid).Distinct().Count() == x.Count(y => y.Uid != Guid.Empty))
            .WithMessage("同一张出库单不能重复产品或明细标识。");
    }

    /// <summary>与数据库 decimal(18,4) 的正数边界保持一致。</summary>
    private static bool ValidQuantity(decimal value) => value > 0 && value <= 99999999999999.9999m && decimal.Round(value, 4) == value;
}

/// <summary>验证创建出库草稿的命令身份和新增行约束。</summary>
public sealed class CreateOutboundDraftCommandValidator : AbstractValidator<CreateOutboundDraftCommand>
{
    /// <summary>校验 CommandId 与草稿输入，新增行不得携带已有身份。</summary>
    public CreateOutboundDraftCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.Draft).NotNull().WithMessage("草稿不能为空。").SetValidator(new OutboundDraftDataValidator()!);
        RuleFor(x => x.Draft.Lines).Must(x => x is null || x.All(y => y is null || y.Uid == Guid.Empty))
            .When(x => x.Draft is not null).WithMessage("新增草稿行不能携带已有出库行标识。");
    }
}

/// <summary>验证更新出库草稿的命令身份与请求版本格式。</summary>
public sealed class UpdateOutboundDraftCommandValidator : AbstractValidator<UpdateOutboundDraftCommand>
{
    /// <summary>只要求版本为正数，当前数据库版本留在事务内核对。</summary>
    public UpdateOutboundDraftCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
        RuleFor(x => x.Draft).NotNull().WithMessage("草稿不能为空。").SetValidator(new OutboundDraftDataValidator()!);
    }
}

/// <summary>验证删除出库草稿的命令身份、版本格式和原因。</summary>
public sealed class DeleteOutboundDraftCommandValidator : AbstractValidator<DeleteOutboundDraftCommand>
{
    /// <summary>删除原因限制在 1–500 字，单据状态仍由事务内业务校验。</summary>
    public DeleteOutboundDraftCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("请填写删除原因。")
            .MaximumLength(500).WithMessage("请填写不超过 500 字的删除原因。");
    }
}

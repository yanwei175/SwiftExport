using FluentValidation;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Reversal.Validation;

/// <summary>在命令锁前校验取消确认请求的身份、版本和原因格式。</summary>
public sealed class CancelProcurementOrderConfirmationCommandValidator : AbstractValidator<CancelProcurementOrderConfirmationCommand>
{
    /// <summary>原因采用 Trim 后的长度语义，具体资格仍由事务内策略判断。</summary>
    public CancelProcurementOrderConfirmationCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("采购版本必须为正数。");
        RuleFor(x => x.Reason).Must(ValidReason).WithMessage("原因须为 1–500 字。");
    }
    private static bool ValidReason(string? value) => value?.Trim() is { Length: >= 1 and <= 500 };
}

/// <summary>在命令锁前校验整单作废请求的身份、版本和原因格式。</summary>
public sealed class VoidProcurementOrderCommandValidator : AbstractValidator<VoidProcurementOrderCommand>
{
    /// <summary>仅检查请求格式，不提前读取下游或释放资格。</summary>
    public VoidProcurementOrderCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("采购版本必须为正数。");
        RuleFor(x => x.Reason).Must(x => x?.Trim() is { Length: >= 1 and <= 500 }).WithMessage("原因须为 1–500 字。");
    }
}

/// <summary>在命令锁前校验草稿删行请求的身份、版本、原因及行集合格式。</summary>
public sealed class DeleteProcurementDraftLinesCommandValidator : AbstractValidator<DeleteProcurementDraftLinesCommand>
{
    /// <summary>行身份和版本先验，当前存在性与可释放数量留在锁定事实后判断。</summary>
    public DeleteProcurementDraftLinesCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("采购版本必须为正数。");
        RuleFor(x => x.Reason).Must(x => x?.Trim() is { Length: >= 1 and <= 500 }).WithMessage("原因须为 1–500 字。");
        RuleFor(x => x.Lines).Must(x => x is { Count: > 0 }
            && x.All(y => y is not null && y.PurchaseOrderLineId != Guid.Empty && y.Version > 0)
            && x.Select(y => y.PurchaseOrderLineId).Distinct().Count() == x.Count)
            .WithMessage("请选择有效且不重复的采购行及版本。");
    }
}

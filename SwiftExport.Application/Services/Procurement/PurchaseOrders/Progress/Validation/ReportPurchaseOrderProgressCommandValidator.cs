using FluentValidation;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderProgress;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Progress.Validation;

/// <summary>在公共回执访问前验证供应商反馈命令的基础格式。</summary>
public sealed class ReportPurchaseOrderProgressCommandValidator : AbstractValidator<ReportPurchaseOrderProgressCommand>
{
    /// <summary>保留既有命令身份、版本、数量、文本和至少一项反馈的输入边界。</summary>
    public ReportPurchaseOrderProgressCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.PurchaseOrderLineId).NotEmpty().WithMessage("采购行标识不能为空。");
        RuleFor(x => x.ExpectedPurchaseOrderVersion).GreaterThan(0).WithMessage("采购单版本无效。");
        RuleFor(x => x.ExpectedLineVersion).GreaterThan(0).WithMessage("采购行版本无效。");
        RuleFor(x => x).Must(x => x.ReadyQty.HasValue || x.ExpectedReadyDate.HasValue || !string.IsNullOrWhiteSpace(x.Note))
            .WithMessage("请至少填写累计已备好数量、预计交期或跟进备注中的一项。");
        RuleFor(x => x.ReadyQty).GreaterThanOrEqualTo(0).When(x => x.ReadyQty.HasValue)
            .WithMessage("累计已备好数量不能小于零。");
        RuleFor(x => x.Note).MaximumLength(2000).WithMessage("跟进备注不能超过 2000 个字符。");
        RuleFor(x => x.CorrectionReason).MaximumLength(2000).WithMessage("纠错原因不能超过 2000 个字符。");
    }
}

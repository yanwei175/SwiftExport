using FluentValidation;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Confirmation;

/// <summary>售后取消确认在回执前执行的纯输入验证。</summary>
public sealed class CancelAfterSalesDemandConfirmationCommandValidator
    : AbstractValidator<CancelAfterSalesDemandConfirmationCommand>
{
    /// <summary>不查询状态，也不改写原原因。</summary>
    public CancelAfterSalesDemandConfirmationCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

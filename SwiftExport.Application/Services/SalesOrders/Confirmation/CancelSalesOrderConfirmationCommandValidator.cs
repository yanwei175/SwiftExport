using FluentValidation;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Application.Services.SalesOrders.Confirmation;

/// <summary>校验销售取消确认请求的纯输入，状态与来源证明留在事务内。</summary>
public sealed class CancelSalesOrderConfirmationCommandValidator : AbstractValidator<CancelSalesOrderConfirmationCommand>
{
    /// <summary>原原因 Trim 语义由命令执行方法保留。</summary>
    public CancelSalesOrderConfirmationCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Uid).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

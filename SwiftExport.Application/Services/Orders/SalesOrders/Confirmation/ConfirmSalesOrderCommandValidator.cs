using FluentValidation;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Application.Services.Orders.SalesOrders.Confirmation;

/// <summary>在销售确认事务前校验命令身份和请求版本的纯输入结构。</summary>
public sealed class ConfirmSalesOrderCommandValidator : AbstractValidator<ConfirmSalesOrderCommand>
{
    /// <summary>仅检查稳定输入；订单状态与版本仍在锁定后重验。</summary>
    public ConfirmSalesOrderCommandValidator()
    {
        RuleFor(command => command.Uid).NotEmpty();
        RuleFor(command => command.CommandId).NotEmpty();
        RuleFor(command => command.Version).GreaterThan(0);
    }
}

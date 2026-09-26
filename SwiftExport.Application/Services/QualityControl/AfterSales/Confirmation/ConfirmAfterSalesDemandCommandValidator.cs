using FluentValidation;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Confirmation;

/// <summary>在售后确认授权后、事务前验证命令标识和头版本的纯请求结构。</summary>
public sealed class ConfirmAfterSalesDemandCommandValidator : AbstractValidator<ConfirmAfterSalesDemandCommand>
{
    /// <summary>不读取可变头行或来源事实；业务版本在锁定后校验。</summary>
    public ConfirmAfterSalesDemandCommandValidator()
    {
        RuleFor(command => command.CommandId).NotEmpty();
        RuleFor(command => command.Version).GreaterThan(0);
    }
}

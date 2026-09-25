using FluentValidation;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Lifecycle;

/// <summary>售后结案请求的纯输入验证。</summary>
public sealed class CloseAfterSalesCommandValidator : AbstractValidator<CloseAfterSalesCommand>
{
    /// <summary>版本和原因资格与原命令一致。</summary>
    public CloseAfterSalesCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>售后重开请求的纯输入验证。</summary>
public sealed class ReopenAfterSalesCommandValidator : AbstractValidator<ReopenAfterSalesCommand>
{
    /// <summary>不访问单据事实。</summary>
    public ReopenAfterSalesCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

/// <summary>售后作废请求的纯输入验证。</summary>
public sealed class VoidAfterSalesCommandValidator : AbstractValidator<VoidAfterSalesCommand>
{
    /// <summary>不访问活动来源证明。</summary>
    public VoidAfterSalesCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(2000);
    }
}

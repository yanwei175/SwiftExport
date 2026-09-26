using FluentValidation;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;

namespace SwiftExport.Application.Services.Inventory.Outbounds.Validation;

/// <summary>验证人工出库过账命令的身份和版本格式，业务资格留在事务内重验。</summary>
public sealed class PostOutboundCommandValidator : AbstractValidator<PostOutboundCommand>
{
    /// <summary>要求稳定命令身份与正期望版本。</summary>
    public PostOutboundCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
    }
}

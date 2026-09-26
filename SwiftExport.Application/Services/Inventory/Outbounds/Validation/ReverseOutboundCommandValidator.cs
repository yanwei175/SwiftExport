using FluentValidation;
using SwiftExport.Contracts.DTOs.Inventory.Outbounds;

namespace SwiftExport.Application.Services.Inventory.Outbounds.Validation;

/// <summary>验证人工出库整单反向的身份、版本及原样原因格式。</summary>
public sealed class ReverseOutboundCommandValidator : AbstractValidator<ReverseOutboundCommand>
{
    /// <summary>维持原因非空且原始长度不超过 500 字的现有准入。</summary>
    public ReverseOutboundCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
        RuleFor(x => x.Reason).Must(x => !string.IsNullOrWhiteSpace(x) && x.Length <= 500)
            .WithMessage("请填写反过账原因（最多 500 字）。");
    }
}

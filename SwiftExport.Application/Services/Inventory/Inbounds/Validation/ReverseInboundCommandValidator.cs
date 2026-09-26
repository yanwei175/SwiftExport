using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Reversal;
using FluentValidation;

namespace SwiftExport.Application.Services.Inventory.Inbounds.Validation;

/// <summary>验证人工入库反向请求的身份、版本与规范化原因格式。</summary>
public sealed class ReverseInboundCommandValidator : AbstractValidator<ReverseInboundCommand>
{
    /// <summary>原因先 Trim 再检查长度；当前状态和余额留在事务内校验。</summary>
    public ReverseInboundCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
        RuleFor(x => x.Reason).Must(x => !string.IsNullOrWhiteSpace(x?.Trim()) && x.Trim().Length <= 500)
            .WithMessage("请填写反过账原因（最多 500 字）。");
    }
}

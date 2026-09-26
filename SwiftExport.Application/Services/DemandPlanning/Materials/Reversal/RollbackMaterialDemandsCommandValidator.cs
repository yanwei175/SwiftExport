using FluentValidation;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Reversal;

/// <summary>在授权后、事务前验证整批回退请求的身份、修订、原因和重复项。</summary>
public sealed class RollbackMaterialDemandsCommandValidator : AbstractValidator<RollbackMaterialDemandsCommand>
{
    /// <summary>仅检查稳定的输入结构；活动修订和下游执行仍在业务锁内重验。</summary>
    public RollbackMaterialDemandsCommandValidator()
    {
        RuleFor(command => command.CommandId).NotEmpty().WithMessage("命令标识和回退项不能为空。");
        RuleFor(command => command.Items).NotNull().NotEmpty().WithMessage("命令标识和回退项不能为空。");
        RuleFor(command => command.Reason).Must(reason => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500)
            .WithMessage("回退原因不能为空且不能超过 500 个字符。");
        RuleFor(command => command.Items).Must(items => items is null ||
            items.All(item => item is not null && item.GrossDemandId != Guid.Empty && item.DecompositionRevision > 0)
            && items.Select(item => item.GrossDemandId).Distinct().Count() == items.Count)
            .WithMessage("命令包含无效或重复的物料修订。");
    }
}

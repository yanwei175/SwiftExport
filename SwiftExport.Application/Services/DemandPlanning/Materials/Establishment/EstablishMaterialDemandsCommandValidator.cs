using FluentValidation;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.Establishment;

/// <summary>在授权后、事务前验证整批建立请求的身份、来源版本和重复项。</summary>
public sealed class EstablishMaterialDemandsCommandValidator : AbstractValidator<EstablishMaterialDemandsCommand>
{
    /// <summary>建立只涉及纯请求结构的校验规则，不读取变化中的业务事实。</summary>
    public EstablishMaterialDemandsCommandValidator()
    {
        RuleFor(command => command.CommandId).NotEmpty().WithMessage("命令标识和来源需求不能为空。");
        RuleFor(command => command.Items).NotNull().NotEmpty().WithMessage("命令标识和来源需求不能为空。");
        RuleFor(command => command.Items).Must(items => items is null ||
            items.All(item => item is not null && item.GrossDemandId != Guid.Empty && item.SourceDemandVersion > 0)
            && items.Select(item => item.GrossDemandId).Distinct().Count() == items.Count)
            .WithMessage("命令包含无效或重复的来源需求。");
    }
}

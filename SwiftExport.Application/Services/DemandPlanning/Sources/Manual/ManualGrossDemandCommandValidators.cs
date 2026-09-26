using FluentValidation;
using SwiftExport.Contracts.DTOs.DemandPlanning.GrossDemand;

namespace SwiftExport.Application.Services.DemandPlanning.Sources.Manual;

/// <summary>在事务前验证手工来源创建命令的稳定身份及纯输入结构。</summary>
public sealed class CreateManualGrossDemandRequestValidator : AbstractValidator<CreateManualGrossDemandRequest>
{
    /// <summary>仅拒绝空命令身份；产品及来源规则继续在原业务步骤中校验。</summary>
    public CreateManualGrossDemandRequestValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
    }
}

/// <summary>在事务前验证手工来源修改命令的稳定身份及纯输入结构。</summary>
public sealed class UpdateManualGrossDemandRequestValidator : AbstractValidator<UpdateManualGrossDemandRequest>
{
    /// <summary>版本和产品事实继续在原事务内重验。</summary>
    public UpdateManualGrossDemandRequestValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
    }
}

/// <summary>在事务前验证手工来源关闭命令的稳定身份及纯输入结构。</summary>
public sealed class CloseGrossDemandCommandValidator : AbstractValidator<CloseGrossDemandCommand>
{
    /// <summary>关闭资格和版本继续在原事务内重验。</summary>
    public CloseGrossDemandCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
    }
}

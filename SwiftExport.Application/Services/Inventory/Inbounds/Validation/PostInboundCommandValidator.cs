using SwiftExport.Contracts.DTOs.Inventory.Inbounds.Posting;
using FluentValidation;

namespace SwiftExport.Application.Services.Inventory.Inbounds.Validation;

/// <summary>仅验证正式入库过账请求的命令身份和版本格式。</summary>
public sealed class PostInboundCommandValidator : AbstractValidator<PostInboundCommand>
{
    /// <summary>路由中的入库单身份由用例单独校验，数据库版本留在事务内复核。</summary>
    public PostInboundCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("ExpectedVersion 必须为正数。");
    }
}

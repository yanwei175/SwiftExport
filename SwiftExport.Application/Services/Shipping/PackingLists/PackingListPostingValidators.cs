using FluentValidation;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>验证过账请求身份与基本格式；业务事实和令牌仍在锁定后重验。</summary>
public sealed class PostPackingListCommandValidator : AbstractValidator<PostPackingListCommand>
{
    /// <summary>拦截缺失的命令标识、版本、日期和令牌。</summary>
    public PostPackingListCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("缺少稳定命令身份或预览令牌。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("箱单身份、版本或业务日期无效。");
        RuleFor(x => x.BusinessDate).NotEqual(default(DateTime)).WithMessage("箱单身份、版本或业务日期无效。");
        RuleFor(x => x.PreviewToken).NotEmpty().WithMessage("缺少稳定命令身份或预览令牌。");
    }
}

/// <summary>验证整单反向请求格式；原执行事实与反向资格由业务方法判断。</summary>
public sealed class ReversePackingListCommandValidator : AbstractValidator<ReversePackingListCommand>
{
    /// <summary>拦截缺失的身份、版本、日期、原因和令牌。</summary>
    public ReversePackingListCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("缺少反向命令身份或预览令牌。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("反向请求身份、版本或业务日期无效。");
        RuleFor(x => x.BusinessDate).NotEqual(default(DateTime)).WithMessage("反向请求身份、版本或业务日期无效。");
        // 与反向预览及事务入口一致：只按规范化后的长度判断，不在验证器中修改原请求。
        RuleFor(x => x.Reason).Must(x => !string.IsNullOrWhiteSpace(x) && x.Trim().Length <= 2000)
            .WithMessage("反向原因必须填写且不能超过 2000 字符。");
        RuleFor(x => x.PreviewToken).NotEmpty().WithMessage("缺少反向命令身份或预览令牌。");
    }
}

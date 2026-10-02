using FluentValidation;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Association.Validation;

/// <summary>关联命令进入公共执行器前校验基础结构；实时资格和数量守恒仍在事务内复核。</summary>
public sealed class AssociateProcurementDemandsCommandValidator : AbstractValidator<AssociateProcurementDemandsCommand>
{
    /// <summary>校验命令身份、预览依据、原因和贡献集合，避免不完整请求占用业务锁。</summary>
    public AssociateProcurementDemandsCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("采购版本必须为正数。");
        RuleFor(x => x.PreviewToken).NotEmpty().WithMessage("关联预览依据不能为空。");
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(500).WithMessage("关联原因必填，最多500字。");
        RuleFor(x => x.Items).Must(x => x is { Count: >= 1 and <= 500 } && x.All(y => y is not null))
            .WithMessage("须提交1至500条有效关联贡献。");
    }
}

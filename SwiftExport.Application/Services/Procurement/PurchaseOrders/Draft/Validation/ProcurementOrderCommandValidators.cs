using FluentValidation;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.Validation;

/// <summary>在命令锁前验证独立采购创建请求的基础结构。</summary>
public sealed class CreateIndependentPurchaseOrderCommandValidator : AbstractValidator<CreateIndependentPurchaseOrderCommand>
{
    /// <summary>限制命令身份、日期与采购行数量；业务资格仍在事务内核对。</summary>
    public CreateIndependentPurchaseOrderCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.OrderDate).NotEqual(default(DateTime)).WithMessage("订单日期必填。");
        RuleFor(x => x.Lines).Must(x => x is { Count: >= 1 and <= 500 } && x.All(y => y is not null))
            .WithMessage("须提交1至500个有效采购行。");
    }
}

/// <summary>在命令锁前验证采购商务保存请求的基础身份。</summary>
public sealed class UpdatePurchaseOrderDraftCommandValidator : AbstractValidator<UpdatePurchaseOrderDraftCommand>
{
    /// <summary>版本格式先行验证，当前头行版本仍由事务内规则核对。</summary>
    public UpdatePurchaseOrderDraftCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("采购版本必须为正数。");
        RuleFor(x => x.Lines).NotNull().WithMessage("采购行不能为空。");
    }
}

/// <summary>在命令锁前验证采购确认请求的基础身份和确认依据格式。</summary>
public sealed class ConfirmProcurementOrderCommandValidator : AbstractValidator<ConfirmProcurementOrderCommand>
{
    /// <summary>确认依据的当前值仍在锁定采购事实后由确认构造器核对。</summary>
    public ConfirmProcurementOrderCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("采购版本必须为正数。");
        RuleFor(x => x.ContextFingerprint).NotEmpty().WithMessage("确认依据不能为空。");
    }
}

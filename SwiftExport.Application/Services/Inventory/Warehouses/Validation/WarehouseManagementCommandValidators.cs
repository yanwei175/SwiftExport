using FluentValidation;
using SwiftExport.Contracts.DTOs.Inventory;

namespace SwiftExport.Application.Services.Inventory.Warehouses.Validation;

/// <summary>验证建仓命令的身份、必填输入及数据库列长度。</summary>
public sealed class CreateSupplierVirtualWarehouseCommandValidator : AbstractValidator<CreateSupplierVirtualWarehouseCommand>
{
    /// <summary>在开启业务事务前验证建仓请求结构，不查询供应商或唯一性事实。</summary>
    public CreateSupplierVirtualWarehouseCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令、供应商、仓库代码和名称不能为空。");
        RuleFor(x => x.SupplierId).NotEmpty().WithMessage("命令、供应商、仓库代码和名称不能为空。");
        RuleFor(x => x.Code).NotEmpty().WithMessage("命令、供应商、仓库代码和名称不能为空。")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("命令、供应商、仓库代码和名称不能为空。")
            .Must(x => x is null || x.Trim().Length <= 100).WithMessage("虚拟仓代码不能超过 100 个字符。");
        RuleFor(x => x.Name).NotEmpty().WithMessage("命令、供应商、仓库代码和名称不能为空。")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("命令、供应商、仓库代码和名称不能为空。")
            .Must(x => x is null || x.Trim().Length <= 300).WithMessage("虚拟仓名称不能超过 300 个字符。");
    }
}

/// <summary>验证仓库改名命令的身份、版本和名称长度。</summary>
public sealed class UpdateWarehouseSiteCommandValidator : AbstractValidator<UpdateWarehouseSiteCommand>
{
    /// <summary>验证请求字段，目标仓库的路由身份由执行方法检查。</summary>
    public UpdateWarehouseSiteCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("虚拟仓、命令、版本和名称不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("虚拟仓、命令、版本和名称不能为空。");
        RuleFor(x => x.Name).NotEmpty().WithMessage("虚拟仓、命令、版本和名称不能为空。")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("虚拟仓、命令、版本和名称不能为空。")
            .Must(x => x is null || x.Trim().Length <= 300).WithMessage("虚拟仓名称不能超过 300 个字符。");
    }
}

/// <summary>验证停用仓库命令的身份、版本和原因长度。</summary>
public sealed class DeactivateWarehouseSiteCommandValidator : AbstractValidator<DeactivateWarehouseSiteCommand>
{
    /// <summary>只验证请求结构；停用资格留在事务内锁定后判断。</summary>
    public DeactivateWarehouseSiteCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("虚拟仓、命令、版本和停用原因不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("虚拟仓、命令、版本和停用原因不能为空。");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("虚拟仓、命令、版本和停用原因不能为空。")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("虚拟仓、命令、版本和停用原因不能为空。")
            .Must(x => x is null || x.Trim().Length <= WarehouseSiteLimits.ReasonMaxLength)
            .WithMessage($"停用原因不能超过 {WarehouseSiteLimits.ReasonMaxLength} 个字符。");
    }
}

/// <summary>验证启用仓库命令的身份、版本和原因长度。</summary>
public sealed class ActivateWarehouseSiteCommandValidator : AbstractValidator<ActivateWarehouseSiteCommand>
{
    /// <summary>只验证请求结构；供应商活动仓唯一性在事务内判断。</summary>
    public ActivateWarehouseSiteCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("虚拟仓、命令、版本和启用原因不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("虚拟仓、命令、版本和启用原因不能为空。");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("虚拟仓、命令、版本和启用原因不能为空。")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("虚拟仓、命令、版本和启用原因不能为空。")
            .Must(x => x is null || x.Trim().Length <= WarehouseSiteLimits.ReasonMaxLength)
            .WithMessage($"启用原因不能超过 {WarehouseSiteLimits.ReasonMaxLength} 个字符。");
    }
}

/// <summary>验证删除仓库命令的身份、版本和原因长度。</summary>
public sealed class DeleteWarehouseSiteCommandValidator : AbstractValidator<DeleteWarehouseSiteCommand>
{
    /// <summary>只验证请求结构；库存及来源引用在事务内判断。</summary>
    public DeleteWarehouseSiteCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("虚拟仓、命令、版本和删除原因不能为空。");
        RuleFor(x => x.ExpectedVersion).GreaterThan(0).WithMessage("虚拟仓、命令、版本和删除原因不能为空。");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("虚拟仓、命令、版本和删除原因不能为空。")
            .Must(x => !string.IsNullOrWhiteSpace(x)).WithMessage("虚拟仓、命令、版本和删除原因不能为空。")
            .Must(x => x is null || x.Trim().Length <= WarehouseSiteLimits.ReasonMaxLength)
            .WithMessage($"删除原因不能超过 {WarehouseSiteLimits.ReasonMaxLength} 个字符。");
    }
}

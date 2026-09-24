using FluentValidation;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;

namespace SwiftExport.Application.Services.DemandPlanning.Materials.SupplierSuggestion;

/// <summary>在事务前验证单行推荐供应商修改命令的身份和纯输入结构。</summary>
public sealed class UpdateSuggestedPurchaseSupplierCommandValidator : AbstractValidator<UpdateSuggestedPurchaseSupplierCommand>
{
    /// <summary>供应商有效性和物料版本仍在原业务锁内核对。</summary>
    public UpdateSuggestedPurchaseSupplierCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("命令标识不能为空。");
    }
}

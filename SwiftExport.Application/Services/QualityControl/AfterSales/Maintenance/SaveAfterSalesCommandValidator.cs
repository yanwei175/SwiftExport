using FluentValidation;
using SwiftExport.Contracts.DTOs.QualityControl.AfterSales;

namespace SwiftExport.Application.Services.QualityControl.AfterSales.Maintenance;

/// <summary>售后维护的纯输入校验；引用、状态与版本仍在事务内重验。</summary>
public sealed class SaveAfterSalesCommandValidator : AbstractValidator<SaveAfterSalesCommand>
{
    /// <summary>保持现有头行集合形状及稳定命令身份。</summary>
    public SaveAfterSalesCommandValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Uid).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Header).NotNull();
        RuleFor(x => x.AddedLines).NotNull();
        RuleFor(x => x.UpdatedLines).NotNull();
        RuleFor(x => x.DeletedLines).NotNull();
    }
}

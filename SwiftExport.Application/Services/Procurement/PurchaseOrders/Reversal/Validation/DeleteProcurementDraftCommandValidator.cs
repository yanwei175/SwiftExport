using FluentValidation;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Reversal.Validation;

/// <summary>整单草稿删除的命令身份、头版本、原因及全量行版本格式；历史资格在事务内核验。</summary>
public sealed class DeleteProcurementDraftCommandValidator : AbstractValidator<DeleteProcurementDraftCommand>
{
    /// <summary>拒绝空身份、重复行和无效版本；不在验证器中读取数据库。</summary>
    public DeleteProcurementDraftCommandValidator()
    {
        RuleFor(x=>x.CommandId).NotEmpty();
        RuleFor(x=>x.ExpectedVersion).GreaterThan(0);
        RuleFor(x=>x.Reason).Must(value=>value?.Trim() is {Length:>=1 and <=500}).WithMessage("原因须为1至500字。");
        RuleFor(x=>x.Lines).Must(lines=>lines is {Count:>0} && lines.All(line=>line is not null && line.PurchaseOrderLineId!=Guid.Empty && line.Version>0)
            && lines.Select(line=>line.PurchaseOrderLineId).Distinct().Count()==lines.Count).WithMessage("整单删除必须提交有效且不重复的全部行版本。");
    }
}

using FluentValidation;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Application.Services.SalesOrders.Lifecycle;

/// <summary>校验销售关闭命令的稳定身份、版本及原因形状。</summary>
public sealed class CloseSalesOrderRequestValidator : AbstractValidator<CloseSalesOrderRequest>
{
    /// <summary>原因的规范化仍由业务方法处理；此处不修改传入请求。</summary>
    public CloseSalesOrderRequestValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Uid).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
        RuleFor(x => x.Reason).NotEmpty().MaximumLength(1000);
    }
}

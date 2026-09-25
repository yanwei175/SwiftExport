using FluentValidation;
using SwiftExport.Contracts.DTOs.SalesOrders.Detail;

namespace SwiftExport.Application.Services.Orders.SalesOrders.Draft;

/// <summary>只校验销售草稿保存请求的稳定输入结构，业务事实留在事务内重验。</summary>
public sealed class SaveSalesOrderRequestValidator : AbstractValidator<SaveSalesOrderRequest>
{
    /// <summary>要求每个创建或更新意图携带稳定命令身份和完整头行集合。</summary>
    public SaveSalesOrderRequestValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Header).NotNull();
        RuleFor(x => x.Lines).NotNull();
        RuleFor(x => x.DeletedLines).NotNull();
        When(x => x.Header is not null, () =>
        {
            RuleFor(x => x.Header.CustomerId).NotEmpty();
            RuleFor(x => x.Header.Version).GreaterThan(0).When(x => x.Header.Uid != Guid.Empty);
        });
    }
}

/// <summary>校验销售草稿删除的命令身份和原版本。</summary>
public sealed class SalesOrderCommandRequestValidator : AbstractValidator<SalesOrderCommandRequest>
{
    /// <summary>仅检查纯请求字段。</summary>
    public SalesOrderCommandRequestValidator()
    {
        RuleFor(x => x.CommandId).NotEmpty();
        RuleFor(x => x.Uid).NotEmpty();
        RuleFor(x => x.Version).GreaterThan(0);
    }
}

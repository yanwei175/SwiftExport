using FluentValidation;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>只验证创建命令身份格式；客户、来源和包装规则留在草稿服务事务内。</summary>
public sealed class CreatePackingListDraftCommandValidator : AbstractValidator<CreatePackingListDraftCommand>
{
    /// <summary>拒绝缺少稳定命令标识的创建请求。</summary>
    public CreatePackingListDraftCommandValidator() =>
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
}

/// <summary>只验证更新命令身份格式，版本事实由草稿服务锁定后判断。</summary>
public sealed class UpdatePackingListDraftCommandValidator : AbstractValidator<UpdatePackingListDraftCommand>
{
    /// <summary>拒绝缺少稳定命令标识的更新请求。</summary>
    public UpdatePackingListDraftCommandValidator() =>
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
}

/// <summary>只验证删除命令身份格式，单据状态由草稿服务锁定后判断。</summary>
public sealed class DeletePackingListDraftCommandValidator : AbstractValidator<DeletePackingListDraftCommand>
{
    /// <summary>拒绝缺少稳定命令标识的删除请求。</summary>
    public DeletePackingListDraftCommandValidator() =>
        RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
}

/// <summary>来源分配命令基础身份验证，数量与版本由事务内复核。</summary>
public sealed class AssociatePackingListSourcesCommandValidator : AbstractValidator<AssociatePackingListSourcesCommand>
{
    /// <summary>每个关联意图必须携带稳定命令标识。</summary>
    public AssociatePackingListSourcesCommandValidator() => RuleFor(x => x.CommandId).NotEmpty().WithMessage("CommandId 不能为空。");
}

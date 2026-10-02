using System.Data;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Interfaces.Procurement;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Procurement.OrderManagement;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.DemandPlanning;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.DemandPlanning.Materials.Execution;
using SwiftExport.Core.Interfaces.Procurement;
using SwiftExport.Core.Models.Procurement;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Association;

/// <summary>采购补关联用例；公共执行器拥有事务与回执，业务校验不依赖详情页或库存状态。</summary>
public sealed class ProcurementDemandAssociationService(
    IProcurementDemandAssociationRepository repository, IProcurementOrderFactLockRepository factLocks,
    INetDemandExecutionLinkRepository links, ProcurementConfirmationBuilder confirmation,
    ProcurementOrderReadSupport reads, ProcurementOrderCommandSupport support, CommandPipeline pipeline,
    BusinessCommandExecutor executor, IBusinessOperationLogRepository logs, ICurrentUser user, ICorrelationContext correlation)
    : IProcurementDemandAssociationService
{
    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementDemandAssociationContextDto>> GetContextAsync(Guid uid, CancellationToken ct = default)
        => reads.ReadConsistentAsync("Edit", async () =>
        {
            if (uid==Guid.Empty) throw ProcurementOperationException.Validation("必须选择采购单。", "Uid");
            // 上下文同时展示多行及其数量关系，使用短一致性事务；读取不生成回执或正式贡献。
            var facts = ProcurementOrderCommandSupport.RequireLockedFacts(await factLocks.LockForAssociationAsync(uid, [], ct));
            var head = ProcurementDemandAssociationPolicy.RequireHead(facts);
            return new ProcurementDemandAssociationContextDto
            {
                PurchaseOrderId=head.Uid, OrderNo=head.OrderNo, Version=head.Version, CustomerId=head.CustomerId!.Value,
                CustomerCode=head.CustomerCode, SupplierId=head.SupplierId, SupplierName=head.SupplierName,
                ConfirmationStatus=head.ConfirmationStatus, SourceType=head.DemandSourceType, SourceUid=head.DemandSourceUid, SourceNo=head.DemandSourceNo,
                Lines=facts.Lines.Where(x => !x.IsDeleted).OrderBy(x => x.LineNumber).Select(line =>
                {
                    var current = facts.Links.Where(x => x.TargetType=="PO" && x.TargetHeadId==uid && x.TargetLineId==line.Uid
                        && !x.IsDeleted && x.LinkStatus==DemandExecutionLinkStatusEnum.Active && x.AllocatedQty>x.ReleasedQty).ToArray();
                    var linked = current.Sum(x => x.AllocatedQty-x.ReleasedQty);
                    return new ProcurementDemandAssociationLineDto
                    {
                        PurchaseOrderLineId=line.Uid, Version=line.Version, LineNumber=line.LineNumber, ProductId=line.ProductId,
                        IsNonStandard=!line.ProductId.HasValue, Sku=line.Sku, Description=line.DescriptionZh, Unit=line.UnitSnapshot,
                        Quantity=line.Quantity, UnlinkedQty=line.UnlinkedQty, LinkedQty=linked,
                        BlockReason=!line.UnlinkedQty.HasValue ? "历史未关联数量未知。"
                            : line.UnlinkedQty<0 || linked+line.UnlinkedQty!=line.Quantity ? "采购数量与引用不守恒。"
                            : line.UnlinkedQty==0 ? "已无未关联数量。" : string.IsNullOrWhiteSpace(line.UnitSnapshot) ? "采购单位未知。" : null,
                        Links=current.Select(link =>
                        {
                            var demand=facts.Demands.SingleOrDefault(x => x.Uid==link.NetDemandId);
                            return new ProcurementDemandAssociationLinkDto { LinkId=link.Uid, Version=link.Version, NetDemandId=link.NetDemandId,
                                DemandSku=demand?.SkuSnapshot, Unit=demand?.UnitSnapshot,
                                SourceNo=facts.Sources.SingleOrDefault(x => x.Uid==demand?.GrossDemandId)?.SourceNo, Quantity=link.AllocatedQty-link.ReleasedQty };
                        }).ToList()
                    };
                }).ToList()
            };
        }, ct);

    /// <inheritdoc/>
    public Task<ServiceResult<PagedResponseBase<ProcurementDemandCandidateDto>>> SearchCandidatesAsync(Guid uid, ProcurementDemandCandidateSearchRequest request, CancellationToken ct = default)
        => reads.ReadAsync("Edit", () =>
        {
            if (uid==Guid.Empty || request is null || request.PurchaseOrderLineId==Guid.Empty || request.PageIndex<1 || request.PageSize is <1 or >200
                || request.Keyword?.Length>200 || request.SourceType?.Length>50 || request.SourceUid==Guid.Empty)
                throw ProcurementOperationException.Validation("采购行、分页或筛选参数无效。", "PurchaseOrderLineId");
            return repository.SearchCandidatesAsync(uid, request, ct);
        }, ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementDemandAssociationPreviewDto>> PreviewAsync(Guid uid, PreviewProcurementDemandAssociationRequest request, CancellationToken ct = default)
        => reads.ReadConsistentAsync("Edit", async () =>
        {
            if (request is null) throw ProcurementOperationException.Validation("关联预览输入不能为空。");
            ProcurementDemandAssociationPolicy.ValidateInput(uid, request.ExpectedVersion, request.Items);
            return (await BuildAsync(uid, request.ExpectedVersion, request.Items, ct)).Preview;
        }, ct);

    /// <inheritdoc/>
    public Task<ServiceResult<ProcurementOrderCommandResult>> AssociateAsync(Guid uid, AssociateProcurementDemandsCommand command, CancellationToken ct = default)
        => pipeline.ExecuteAsync<AssociateProcurementDemandsCommand, ProcurementOrderCommandResult>(command, command?.CommandId,
            "Procurement.Order.AssociateDemands", token => support.AuthorizeAsync<ProcurementOrderCommandResult>("Edit", token),
            token => ExecuteAsync(uid, command!, token), ct);

    /// <summary>稳定排序冻结命令输入，权限已先检查；新关联使用新的命令身份，不改变原采购创建回执。</summary>
    private Task<ServiceResult<CommandExecutionResult<ProcurementOrderCommandResult>>> ExecuteAsync(Guid uid, AssociateProcurementDemandsCommand command, CancellationToken ct)
        => support.RunAsync(async () =>
        {
            ProcurementDemandAssociationPolicy.ValidateInput(uid, command.ExpectedVersion, command.Items);
            var reason=ProcurementOrderPolicy.Text(command.Reason, 500, "Reason");
            if (command.CommandId==Guid.Empty || string.IsNullOrWhiteSpace(reason) || string.IsNullOrWhiteSpace(command.PreviewToken))
                throw ProcurementOperationException.Validation("关联命令身份、原因和预览令牌必填。", "Reason");
            return await executor.ExecuteAsync(command.CommandId, "Procurement.Order.AssociateDemands", uid,
                new { command.ExpectedVersion, command.PreviewToken, Reason=reason, Items=command.Items.OrderBy(x => x.CommandItemId).ToArray() },
                token => ApplyInTransactionAsync(uid, command, reason, token), ct, IsolationLevel.Serializable);
        }, ct);

    /// <summary>来源、物料、引用、采购头行锁定后重验；仅本事务可以写入全部贡献和审计。</summary>
    private async Task<ProcurementOrderCommandResult> ApplyInTransactionAsync(Guid uid, AssociateProcurementDemandsCommand command, string reason, CancellationToken ct)
    {
        var (facts, preview)=await BuildAsync(uid, command.ExpectedVersion, command.Items, ct);
        if (preview.Token!=command.PreviewToken)
            throw new ProcurementOperationException(ServiceErrorCodes.VersionConflict, "需求、采购或有效引用已变化，请重新预览。", "PreviewToken");
        if ((await links.GetByCommandItemIdsForUpdateAsync(command.Items.Select(x => x.CommandItemId), ct)).Count>0)
            throw new ProcurementOperationException(ServiceErrorCodes.CommandConflict, "贡献命令身份已使用；新分配不能复活旧引用。", "Items[].CommandItemId");
        var head=facts.Head!;
        var before=JsonSerializer.SerializeToElement(new { head, Lines=facts.Lines, Links=facts.Links });
        var firstDemand=facts.Demands.Single(x => x.Uid==command.Items[0].NetDemandId);
        var source=facts.Sources.Single(x => x.Uid==firstDemand.GrossDemandId);
        head.DemandSourceType=source.SourceType; head.DemandSourceUid=source.SourceUid; head.DemandSourceNo=source.SourceNo;
        var additions=command.Items.Select(item =>
        {
            var demand=facts.Demands.Single(x => x.Uid==item.NetDemandId);
            var line=facts.Lines.Single(x => x.Uid==item.PurchaseOrderLineId);
            return new DemandExecutionLink { Uid=GuidSequentialGenerator.NewGuid(), Version=1, NetDemandId=demand.Uid,
                ExecutionRequestId=command.CommandId, CommandItemId=item.CommandItemId, TargetType="PO", TargetHeadId=uid,
                TargetLineId=line.Uid, TargetNo=head.OrderNo, AllocatedQty=item.Quantity, ReleasedQty=0,
                LinkStatus=DemandExecutionLinkStatusEnum.Active, DemandRequiredDate=demand.RequiredDate, PlannedDeliveryDate=line.PlannedDeliveryDate };
        }).ToArray();
        var changed=new List<SwiftExport.Core.Entities.Procurement.PurchaseOrderLine>();
        foreach (var group in command.Items.GroupBy(x => x.PurchaseOrderLineId))
        {
            var line=facts.Lines.Single(x => x.Uid==group.Key);
            line.UnlinkedQty-=group.Sum(x => x.Quantity);
            line.SourceType=source.SourceType; line.SourceUid=source.SourceUid; line.SourceNo=source.SourceNo;
            var grossIds=facts.Links.Where(x => x.TargetHeadId==uid && x.TargetLineId==line.Uid
                    && !x.IsDeleted && x.LinkStatus==DemandExecutionLinkStatusEnum.Active && x.AllocatedQty>x.ReleasedQty)
                .Select(x => facts.Demands.Single(n => n.Uid==x.NetDemandId).GrossDemandId)
                .Concat(group.Select(x => facts.Demands.Single(n => n.Uid==x.NetDemandId).GrossDemandId)).Distinct().ToArray();
            var single=grossIds.Length==1 ? facts.Sources.Single(x => x.Uid==grossIds[0]) : null;
            line.SourceLineId=single?.SourceLineId; line.SourceLineNumber=single?.SourceLineNumber;
            changed.Add(line);
        }
        ProcurementOrderCommandSupport.EnsureWriteSucceeded(await repository.ApplyAsync(head, changed, ct));
        ct.ThrowIfCancellationRequested();
        if (await links.InsertPurchaseLinksAsync(additions, ct)!=additions.Length)
            throw new InvalidOperationException("采购贡献写入数量不完整。");
        facts.Links.AddRange(additions);
        ProcurementConfirmationBuilder.RequireSourceReferences(facts);
        var result=ProcurementOrderCommandSupport.Result(head, facts.Lines.Where(x => !x.IsDeleted).ToList());
        if (await logs.InsertAsync(new BusinessOperationLog
        {
            Uid=GuidSequentialGenerator.NewGuid(), CommandId=command.CommandId, CorrelationId=correlation.CorrelationId,
            Module="Procurement.OrderManagement", AggregateType="PurchaseOrder", AggregateUid=uid, AggregateNoSnapshot=head.OrderNo,
            OperationType="AssociateDemands", OperationName="采购追加物料需求关联", Reason=reason,
            Summary="保持采购与库存事实，新增需求贡献；原创建和确认身份保留。",
            ChangedFieldsJson=JsonSerializer.Serialize(new { Before=before, After=new { head, Lines=facts.Lines, Links=facts.Links } }),
            RelatedObjectsJson=JsonSerializer.Serialize(new { Sources=facts.Sources, Demands=facts.Demands, Contributions=preview.Items }),
            UserUid=user.UserId, UserNameSnapshot=user.UserName, OccurredDate=DateTime.Now
        })!=1) throw new InvalidOperationException("关联业务日志写入失败。");
        return result;
    }

    /// <summary>预览和提交共用完整规则；先合并新旧来源锁集合，不在采购锁后补取需求锁。</summary>
    private async Task<(ProcurementReversalFacts Facts, ProcurementDemandAssociationPreviewDto Preview)> BuildAsync(
        Guid uid, int version, IReadOnlyList<ProcurementDemandAssociationItem> items, CancellationToken ct)
    {
        var facts=ProcurementOrderCommandSupport.RequireLockedFacts(await factLocks.LockForAssociationAsync(uid, items.Select(x => x.NetDemandId).Distinct().ToArray(), ct));
        var head=ProcurementDemandAssociationPolicy.RequireHead(facts);
        await confirmation.ValidatePartiesAsync(head.CustomerId!.Value, head.SupplierId, ct);
        foreach (var id in items.Select(x => x.PurchaseOrderLineId).Distinct())
        {
            var line=facts.Lines.SingleOrDefault(x => x.Uid==id && !x.IsDeleted)
                ?? throw ProcurementOperationException.Validation("采购行不存在或已删除。", "Items[].PurchaseOrderLineId");
            await confirmation.ValidateLineIdentityAsync(line, head.OriginKind, ct);
        }
        var targets=await repository.ReadTargetsAsync(facts.Links.Where(x => !x.IsDeleted && x.AllocatedQty>x.ReleasedQty).Select(x => x.TargetLineId).Distinct().ToArray(), ct);
        return (facts,ProcurementDemandAssociationPolicy.Build(facts, targets, version, items));
    }
}

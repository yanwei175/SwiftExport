using Mapster;
using SwiftExport.Application.Interfaces.Shipping;
using System.Text.Json;
using SwiftExport.Application.Commands;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Shipping;
namespace SwiftExport.Application.Services.Shipping.PackingLists;

public sealed partial class PackingListDraftService
{
    /// <summary>独立来源关联命令，普通编辑不替换来源；同事务软删原行并保留未分配余量。</summary>
    public Task<ServiceResult<PackingListDraftCommandResult>> AssociateSourcesAsync(Guid uid, Guid lineUid,
        AssociatePackingListSourcesCommand command, CancellationToken token = default) =>
        RunDraftPipelineAsync(command, command?.CommandId, "Shipping.PackingList.Draft.Associate", "Shipping.PackingList.Edit",
            ct => RunAsync(async () =>
            {
                await RequireSchemaAsync(ct);
                ValidateIdentity(uid, command!.CommandId, command.ExpectedVersion);
                if (lineUid == Guid.Empty || command.ExpectedLineVersion <= 0 || command.Allocations == null || command.Allocations.Count is < 1 or > 500
                    || command.Allocations.Any(x => x == null || x.GrossDemandId == Guid.Empty || x.ExpectedDemandVersion <= 0 || !x.ExpectedSourceVersion.HasValue || x.ExpectedSourceVersion <= 0)
                    || command.Allocations.Select(x => x.GrossDemandId).Distinct().Count() != command.Allocations.Count)
                    Fail(PackingListErrorCodes.InvalidDraft, "请提交唯一来源及有效版本。", "Allocations");
                foreach (var item in command.Allocations) ValidateDecimal(item.Qty, false, "Allocations.Qty");
                var generatedIds = new List<Guid>();
                return await ExecuteWriteAsync(command.CommandId, "Associate", uid,
                    JsonSerializer.Serialize(new { lineUid, command.ExpectedVersion, command.ExpectedLineVersion, command.Allocations }), async () =>
                    {
                        var head = await RequireEditableAsync(uid, command.ExpectedVersion, ct);
                        var existing = await repository.LockLinesAsync(uid, ct);
                        var original = existing.SingleOrDefault(x => x.Uid == lineUid);
                        if (original == null || original.Version != command.ExpectedLineVersion)
                            Fail(PackingListErrorCodes.VersionConflict, "待关联行已变化，请刷新。", "ExpectedLineVersion");
                        if (original!.GrossDemandId.HasValue || original.PackingGroupId.HasValue)
                            Fail(PackingListErrorCodes.StateBlocked, "仅普通待关联行允许分配来源。", "LineUid");
                        var allocated = command.Allocations.Sum(x => x.Qty);
                        if (allocated > original.Qty) Fail(PackingListErrorCodes.InvalidDraft, "分配量超过原行数量。", "Allocations.Qty");
                        var totals = existing.Where(x => x.GrossDemandId.HasValue).GroupBy(x => x.GrossDemandId!.Value).ToDictionary(x => x.Key, x => x.Sum(y => y.Qty));
                        foreach (var item in command.Allocations) totals[item.GrossDemandId] = totals.GetValueOrDefault(item.GrossDemandId) + item.Qty;
                        var verified = await sourceDelivery.VerifyForUpdateAsync(totals.Select(x => new SourceDeliveryClaim { GrossDemandId = x.Key, Quantity = x.Value }).ToArray(), ct);
                        var children = new List<PackingListLine>();
                        foreach (var item in command.Allocations)
                        {
                            if (!verified.TryGetValue(item.GrossDemandId, out var source) || source.DemandVersion != item.ExpectedDemandVersion
                                || source.SourceVersion != item.ExpectedSourceVersion || source.CustomerId != head.CustomerId
                                || source.ProductId != original.ProductId || source.IsNonStandard != original.IsNonStandardSnapshot
                                || !SameUnit(source.Unit, original.UnitSnapshot) || source.RemainingQty < totals[item.GrossDemandId])
                                Fail(PackingListErrorCodes.SourceChanged, "来源版本、客户、产品、单位或剩余量已变化，请重新查询。", "Allocations");
                            var child = CopyChild(original, item.Qty); child.GrossDemandId = source.GrossDemandId;
                            child.SourceTypeSnapshot = RequiredSnapshot(source.SourceType, "来源类型不完整。", child.Uid, "SourceTypeSnapshot", 300);
                            child.SourceDocumentNoSnapshot = RequiredSnapshot(source.SourceNo, "来源单号不完整。", child.Uid, "SourceDocumentNoSnapshot", 300);
                            child.SourceLineNumberSnapshot = source.SourceLineNumber;
                            children.Add(child);
                        }
                        if (allocated < original.Qty) children.Add(CopyChild(original, original.Qty - allocated));
                        generatedIds.AddRange(children.Select(x => x.Uid));
                        var active = existing.OrderBy(x => x.LineNumber).SelectMany(x => x.Uid == lineUid ? children : new List<PackingListLine> { x }).ToList();
                        if (active.Count > 500) Fail(PackingListErrorCodes.InvalidDraft, "拆分后明细超过 500 行。", "Allocations");
                        for (var i = 0; i < active.Count; i++) active[i].LineNumber = i + 1;
                        await repository.SaveAsync(head, active, new[] { original }, command.ExpectedVersion, ct);
                        return await ResultAsync(uid, false, ct);
                    }, ct, command.Allocations.Select(x => x.GrossDemandId), auditDetails: new { OriginalLineUid = lineUid, GeneratedLineIds = generatedIds });
            }, ct), token);

    // 拆行清除旧审计和包装数量，避免把原箱数重复分配；产品和 Excel 参考文本保留。
    private static PackingListLine CopyChild(PackingListLine original, decimal qty)
    {
        var child = original.Adapt<PackingListLine>();
        child.Uid = GuidSequentialGenerator.NewGuid(); child.Version = 0; child.IsDeleted = false;
        child.CreatedBy = null; child.CreatedDate = null; child.UpdatedBy = null; child.UpdatedDate = null;
        child.Qty = qty; child.PcsPerPackage = null; child.PackageCount = null;
        child.NwPerPackage = null; child.GwPerPackage = null; child.CbmPerPackage = null;
        child.SubTotalNw = null; child.SubTotalGw = null; child.SubTotalCbm = null;
        return child;
    }
}

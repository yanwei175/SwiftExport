using SwiftExport.Application.Interfaces.Systems.Numbering;
using SwiftExport.Core.Interfaces.Customers.Management;
using Microsoft.Extensions.Logging;
using SwiftExport.Application.Contexts;
using SwiftExport.Application.Commands;
using SwiftExport.Application.Commands.Validation;
using SwiftExport.Application.Interfaces;
using SwiftExport.Application.Interfaces.Auth;
using SwiftExport.Application.Interfaces.Auth.Permissions.Evaluation;
using SwiftExport.Application.Interfaces.Shipping;
using SwiftExport.Application.Interfaces.Shipping.PackingLists;
using SwiftExport.Application.Services.Shipping;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.Auth;
using SwiftExport.Contracts.DTOs.DemandPlanning.SourceDemandDelivery;
using SwiftExport.Contracts.DTOs.Errors;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Contracts.DTOs.SystemDtos.Requests;
using SwiftExport.Contracts.SysEnums.Shipping;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using SwiftExport.Contracts.Wrappers;
using SwiftExport.Core.Entities.Auditing;
using SwiftExport.Core.Entities.Inventory;
using SwiftExport.Core.Entities.Shipping;
using SwiftExport.Core.Interfaces;
using SwiftExport.Core.Interfaces.Auditing;
using SwiftExport.Core.Interfaces.Auth;
using SwiftExport.Core.Interfaces.Inventory;
using SwiftExport.Core.Interfaces.Shipping;
using SwiftExport.Core.Interfaces.Shipping.PackingLists;
using System.Data;
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>编排装箱单草稿命令；草稿不预占交付额度，也不产生库存事实。</summary>
public sealed class PackingListDraftService(
    ServiceBaseContext context,
    IPackingListDocumentRepository repository,
    IPackingListQueryRepository queries,
    IPackingListPostingFactsRepository postingFacts,
    ISourceDeliveryService sourceDelivery,
    ICustomerRepository customers,
    IBusinessOperationLogRepository logs,
    IDocumentNoService numbers,
    ICurrentUser currentUser,
    IPermissionEvaluationService permissions,
    CommandPipeline pipeline,
    BusinessCommandExecutor executor) : IPackingListDraftService
{
    private const string Module = "PackingList";
    private const decimal MaxDecimal = 99999999999999.9999m;
    private readonly ILogger<PackingListDraftService> _logger = context.LoggerFactory.CreateLogger<PackingListDraftService>();

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> CreateAsync(
        CreatePackingListDraftCommand command, CancellationToken cancellationToken = default) =>
        RunDraftPipelineAsync(command, command?.CommandId, "Shipping.PackingList.Draft.Create",
            ct => CreateCoreAsync(command!, ct), cancellationToken);

    /// <summary>验证创建输入并把首次业务执行交给公共事务和回执执行器。</summary>
    private Task<ServiceResult<CommandExecutionResult<PackingListDraftCommandResult>>> CreateCoreAsync(
        CreatePackingListDraftCommand command, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            await RequireSchemaAsync(cancellationToken);
            if (command is null || command.CommandId == Guid.Empty)
                Fail(PackingListErrorCodes.InvalidDraft, "CommandId 不能为空。", "CommandId");
            if (command!.Uid == Guid.Empty) Fail(PackingListErrorCodes.InvalidDraft, "装箱单 Uid 不能为空。", "Uid");
            ValidateHeader(command.Header);
            ValidateLineInputs(command.Lines, Array.Empty<PackingListDeletedLineInput>());
            if (command.Lines.Any(row => row.ExpectedVersion != 0))
                Fail(PackingListErrorCodes.LineSetMismatch, "创建草稿的全部行 ExpectedVersion 必须为 0。", "Lines.ExpectedVersion");
            var fingerprint = Fingerprint("Create", command.Uid, 0, command.Header, command.Lines, Array.Empty<PackingListDeletedLineInput>());
            return await ExecuteWriteAsync(command.CommandId, "Create", command.Uid, fingerprint, async () =>
            {
                if (await repository.LockHeaderAsync(command.Uid, true, cancellationToken) is not null)
                    Fail(PackingListErrorCodes.CommandConflict, "该装箱单 Uid 已由另一创建意图占用。", "Uid");
                var customer = await RequireCustomerAsync(command.Header.CustomerId, true);
                var header = new PackingListHead
                {
                    Uid = command.Uid,
                    Version = 1,
                    LifecycleStatus = PackingListLifecycleStatusEnum.Active,
                    PostingStatus = PackingListPostingStatusEnum.Unposted,
                    CreateCommandId = command.CommandId
                };
                ApplyHeader(header, command.Header);
                if (string.IsNullOrWhiteSpace(header.CustomerNameSnapshot))
                    header.CustomerNameSnapshot = customer.CustomerName?.Trim();
                var owners = await repository.LockLineOwnersAsync(command.Lines.Select(row => row.Uid).ToArray(), cancellationToken);
                if (owners.Count != 0)
                    Fail(PackingListErrorCodes.LineSetMismatch, "创建行 Uid 已被其他单据或历史行占用。", "Lines.Uid");
                var number = await numbers.GetNextDocumentNoAsync(new DocumentNoRequest
                {
                    DocType = DocumentTypeStaticsEnum.PackingList,
                    BusinessDate = header.InvoiceDate,
                    Operator = currentUser.UserName
                });
                if (!number.Success || string.IsNullOrWhiteSpace(number.Data?.DocumentNo))
                    Fail(PackingListErrorCodes.StateBlocked, number.Message ?? "装箱单号生成失败，草稿未保存。");
                header.PackingListNo = number.Data!.DocumentNo.Trim();
                var lines = await BuildLinesAsync(header, command.Lines, Array.Empty<PackingListLine>(), cancellationToken);
                await repository.SaveAsync(header, lines, Array.Empty<PackingListLine>(), 0, cancellationToken);
                return await ResultAsync(header.Uid, false, cancellationToken);
            }, cancellationToken, command.Lines.Select(x => x.GrossDemandId), command.Lines.Select(x => x.PackingGroupId).OfType<Guid>());
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> UpdateAsync(
        Guid uid, UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default) =>
        RunDraftPipelineAsync(command, command?.CommandId, "Shipping.PackingList.Draft.Update",
            ct => UpdateCoreAsync(uid, command!, ct), cancellationToken);

    /// <summary>验证更新输入并在同一事务内重验版本、来源和完整明细集合。</summary>
    private Task<ServiceResult<CommandExecutionResult<PackingListDraftCommandResult>>> UpdateCoreAsync(
        Guid uid, UpdatePackingListDraftCommand command, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            await RequireSchemaAsync(cancellationToken);
            ValidateIdentity(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0);
            ValidateHeader(command!.Header);
            ValidateLineInputs(command.Lines, command.DeletedLines);
            var fingerprint = Fingerprint("Update", uid, command.ExpectedVersion, command.Header, command.Lines, command.DeletedLines);
            return await ExecuteWriteAsync(command.CommandId, "Update", uid, fingerprint, async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, cancellationToken);
                var existing = await repository.LockLinesAsync(uid, cancellationToken);
                ValidateLineSet(existing, command.Lines, command.DeletedLines);
                var owners = await repository.LockLineOwnersAsync(command.Lines.Select(row => row.Uid)
                    .Concat(command.DeletedLines.Select(row => row.Uid)).ToArray(), cancellationToken);
                if (owners.Any(pair => pair.Value != uid))
                    Fail(PackingListErrorCodes.LineSetMismatch, "行 Uid 属于其他装箱单。", $"Lines[{owners.First(pair => pair.Value != uid).Key}]");
                var historicalReuse = command.Lines.FirstOrDefault(input =>
                    existing.All(row => row.Uid != input.Uid) && owners.ContainsKey(input.Uid));
                if (historicalReuse is not null)
                    Fail(PackingListErrorCodes.LineSetMismatch, "已删除行的 Uid 不能作为新行复用。", $"Lines[{historicalReuse.Uid}].Uid");
                if (existing.Count > 0 && header.CustomerId != command.Header.CustomerId)
                    Fail(PackingListErrorCodes.CustomerMismatch, "已有物理明细时不能更换客户，请重新建单。", "Header.CustomerId");
                await RequireCustomerAsync(command.Header.CustomerId, header.CustomerId != command.Header.CustomerId);
                ApplyHeader(header, command.Header);
                var lines = await BuildLinesAsync(header, command.Lines, existing, cancellationToken);
                var deleted = command.DeletedLines.Select(input => existing.Single(row => row.Uid == input.Uid)).ToArray();
                await repository.SaveAsync(header, lines, deleted, command.ExpectedVersion, cancellationToken);
                return await ResultAsync(uid, false, cancellationToken);
            }, cancellationToken, command.Lines.Select(x => x.GrossDemandId), command.Lines.Select(x => x.PackingGroupId).OfType<Guid>());
        }, cancellationToken);

    /// <inheritdoc/>
    public Task<ServiceResult<PackingListDraftCommandResult>> DeleteAsync(
        Guid uid, DeletePackingListDraftCommand command, CancellationToken cancellationToken = default) =>
        RunDraftPipelineAsync(command, command?.CommandId, "Shipping.PackingList.Draft.Delete",
            ct => DeleteCoreAsync(uid, command!, ct), cancellationToken);

    /// <summary>验证删除身份，并在事务内重验草稿状态后软删除。</summary>
    private Task<ServiceResult<CommandExecutionResult<PackingListDraftCommandResult>>> DeleteCoreAsync(
        Guid uid, DeletePackingListDraftCommand command, CancellationToken cancellationToken = default) =>
        RunAsync(async () =>
        {
            await RequireSchemaAsync(cancellationToken);
            ValidateIdentity(uid, command?.CommandId ?? Guid.Empty, command?.ExpectedVersion ?? 0);
            var fingerprint = Fingerprint("Delete", uid, command!.ExpectedVersion, null,
                Array.Empty<PackingListDraftLineInput>(), Array.Empty<PackingListDeletedLineInput>());
            return await ExecuteWriteAsync(command.CommandId, "Delete", uid, fingerprint, async () =>
            {
                var header = await RequireEditableAsync(uid, command.ExpectedVersion, cancellationToken);
                await repository.LockLinesAsync(uid, cancellationToken);
                await repository.DeleteAsync(header, cancellationToken);
                return new PackingListDraftCommandResult { Uid = uid, AlreadyApplied = false, Detail = null };
            }, cancellationToken);
        }, cancellationToken);

    /// <summary>建立或更新行模型；新行采用来源快照，已有行保留历史来源快照。</summary>
    private async Task<IReadOnlyList<PackingListLine>> BuildLinesAsync(
        PackingListHead header, IReadOnlyList<PackingListDraftLineInput> inputs,
        IReadOnlyList<PackingListLine> existing, CancellationToken token)
    {
        var existingById = existing.ToDictionary(row => row.Uid);
        var totals = inputs.GroupBy(row => row.GrossDemandId).ToDictionary(group => group.Key, group => group.Sum(row => row.Qty));
        IReadOnlyDictionary<Guid, SourceDemandDeliveryItemDto> sources;
        try
        {
            sources = await sourceDelivery.VerifyForUpdateAsync(totals.Select(pair => new SourceDeliveryClaim
            {
                GrossDemandId = pair.Key,
                Quantity = pair.Value
            }).ToArray(), token);
        }
        catch (SourceDeliveryException ex)
        {
            var mapped = ex.Code == "DELIVERY_QUERY_UNAVAILABLE"
                ? PackingListErrorCodes.DeliveryUnavailable
                : PackingListErrorCodes.SourceChanged;
            throw new PackingListException(mapped, ex.Message);
        }
        var enrichments = (await queries.GetSourceEnrichmentsAsync(totals.Keys.ToArray(), token))
            .ToDictionary(item => item.GrossDemandId);
        foreach (var total in totals)
        {
            if (!sources.TryGetValue(total.Key, out var source))
                Fail(PackingListErrorCodes.SourceChanged, "来源需求不存在、已关闭或已不再适用于出货。", $"Lines[{total.Key}].GrossDemandId");
            if (source.CustomerId != header.CustomerId)
                Fail(PackingListErrorCodes.CustomerMismatch, "装箱单中存在其他客户的来源需求。", $"Lines[{total.Key}].GrossDemandId");
            if (total.Value > source.RemainingQty)
                Fail(PackingListErrorCodes.SourceChanged, "本单来源数量超过当前剩余可交付量。", $"Lines[{total.Key}].Qty");
        }

        var result = new List<PackingListLine>(inputs.Count);
        foreach (var input in inputs)
        {
            var source = sources[input.GrossDemandId];
            var enrichment = enrichments.GetValueOrDefault(input.GrossDemandId);
            PackingListLine line;
            if (existingById.TryGetValue(input.Uid, out var old))
            {
                if (old.GrossDemandId != input.GrossDemandId || old.ProductId != input.ProductId
                    || old.ProductId != source.ProductId || old.IsNonStandardSnapshot != source.IsNonStandard
                    || !string.Equals(old.SourceTypeSnapshot, source.SourceType, StringComparison.Ordinal)
                    || !string.Equals(old.SourceDocumentNoSnapshot, source.SourceNo, StringComparison.Ordinal)
                    || old.SourceLineNumberSnapshot != source.SourceLineNumber)
                    Fail(PackingListErrorCodes.SourceChanged, "已有行的来源或产品身份已变化。", $"Lines[{input.Uid}].GrossDemandId");
                line = old;
            }
            else
            {
                if (input.ProductId != source.ProductId)
                    Fail(PackingListErrorCodes.SourceChanged, "所选来源的产品身份已变化。", $"Lines[{input.Uid}].ProductId");
                line = new PackingListLine
                {
                    Uid = input.Uid,
                    Version = 0,
                    PackingListHeadId = header.Uid,
                    GrossDemandId = source.GrossDemandId,
                    ProductId = source.ProductId,
                    IsNonStandardSnapshot = source.IsNonStandard,
                    SourceTypeSnapshot = RequiredSnapshot(source.SourceType, "来源类型不完整。", input.Uid, "SourceTypeSnapshot", 300),
                    SourceDocumentNoSnapshot = RequiredSnapshot(source.SourceNo, "来源单号不完整。", input.Uid, "SourceDocumentNoSnapshot", 300),
                    SourceLineNumberSnapshot = source.SourceLineNumber,
                    SkuSnapshot = source.IsNonStandard
                        ? Clean(source.Sku, 300) ?? string.Empty
                        : RequiredSnapshot(source.Sku, "来源产品编码不完整。", input.Uid, "SkuSnapshot"),
                    LinkSkuSnapshot = Clean(source.LinkSku, 200),
                    CustomerSkuSnapshot = Clean(source.CustomerSku, 200),
                    DescriptionEnSnapshot = Clean(source.Description, 2000),
                    ColorNameEnSnapshot = Clean(source.Color, 100),
                    UnitSnapshot = source.IsNonStandard
                        ? Clean(source.Unit, 100)
                        : RequiredSnapshot(source.Unit, "来源计量单位不完整。", input.Uid, "UnitSnapshot"),
                    CustomerPoNo = Clean(enrichment?.CustomerPoNo, 200)
                };
            }
            line.LineNumber = input.LineNumber;
            line.PackingGroupId = input.PackingGroupId;
            line.Qty = input.Qty;
            // 包装是本单可编辑事实；尾箱空值不能被主数据默认包装覆盖。
            line.PcsPerPackage = input.PackingGroupId.HasValue ? input.Qty : input.PcsPerPackage;
            line.NwPerPackage = input.NwPerPackage;
            line.GwPerPackage = input.GwPerPackage;
            line.CbmPerPackage = input.CbmPerPackage;
            line.PackageCount = input.PackingGroupId.HasValue ? 1 : CalculatePackageCount(input.Qty, input.PcsPerPackage);
            line.SubTotalNw = Multiply(line.PackageCount, line.NwPerPackage);
            line.SubTotalGw = Multiply(line.PackageCount, line.GwPerPackage);
            line.SubTotalCbm = Multiply(line.PackageCount, line.CbmPerPackage);
            line.InvoiceNo = Clean(input.InvoiceNo, 200);
            line.Marks = Clean(input.Marks, 2000);
            line.Note = Clean(input.Note, 2000);
            result.Add(line);
        }
        return result;
    }

    /// <summary>事务外只探测来源锁集合；公共执行器在可串行化事务内持有命令锁、回执与提交权。</summary>
    private async Task<CommandExecutionResult<PackingListDraftCommandResult>> ExecuteWriteAsync(Guid commandId, string operation,
        Guid aggregateUid, string fingerprint, Func<Task<PackingListDraftCommandResult>> action, CancellationToken token,
        IEnumerable<Guid>? incomingSourceIds = null, IEnumerable<Guid>? incomingGroupIds = null)
    {
        var observed = await repository.GetDetailAsync(aggregateUid, token);
        var sourceIds = (observed?.Lines.Select(x => x.GrossDemandId) ?? Enumerable.Empty<Guid>())
            .Concat(incomingSourceIds ?? Enumerable.Empty<Guid>()).Distinct().Order().ToArray();
        var executed = await executor.ExecuteAsync(commandId, $"Shipping.PackingList.Draft.{operation}", aggregateUid,
            new { Fingerprint = fingerprint }, async ct =>
            {
                // 与原草稿命令一致：先确认拼箱身份、锁来源，再进入箱单头行及明细写入。
                var groupIds = incomingGroupIds?.Distinct().Order().ToArray() ?? [];
                if (groupIds.Length > 0 && await repository.HasForeignPackingGroupsAsync(aggregateUid, groupIds, ct))
                    Fail(PackingListErrorCodes.InvalidDraft, "拼箱标识已被其他箱单使用。", "Lines.PackingGroupId");
                await postingFacts.LockSourcesAsync(sourceIds, ct);
                var result = await action();
                if (await logs.InsertAsync(new BusinessOperationLog
                {
                    Uid = GuidSequentialGenerator.NewGuid(), CommandId = commandId, CorrelationId = commandId.ToString("N"),
                    Module = Module, AggregateType = "PackingListHead", AggregateUid = aggregateUid,
                    AggregateNoSnapshot = result.Detail?.Header?.PackingListNo, OperationType = operation,
                    OperationName = operation switch { "Create" => "创建装箱单草稿", "Update" => "更新装箱单草稿", _ => "删除装箱单草稿" },
                    Summary = "装箱单头、行与命令回执在同一事务提交；未产生库存事实。",
                    ChangedFieldsJson = JsonSerializer.Serialize(new { Fingerprint = fingerprint }),
                    RelatedObjectsJson = JsonSerializer.Serialize(result), UserUid = currentUser.UserId,
                    UserNameSnapshot = currentUser.UserName, OccurredDate = DateTime.Now
                }) != 1) Fail(PackingListErrorCodes.StateBlocked, "业务日志写入失败，草稿已回滚。");
                return result;
            }, token, IsolationLevel.Serializable);
        executed.Value.AlreadyApplied = executed.IsReplay;
        return executed;
    }
    private async Task<PackingListHead> RequireEditableAsync(Guid uid, int expectedVersion, CancellationToken token)
    {
        var header = await repository.LockHeaderAsync(uid, false, token)
            ?? throw new PackingListException(PackingListErrorCodes.NotFound, "装箱单不存在或已删除。");
        if (header.Version != expectedVersion)
            Fail(PackingListErrorCodes.VersionConflict, "装箱单已被其他用户修改，请刷新后重试。", "ExpectedVersion");
        if (header.LifecycleStatus != PackingListLifecycleStatusEnum.Active
            || header.PostingStatus != PackingListPostingStatusEnum.Unposted)
            Fail(PackingListErrorCodes.StateBlocked, "只有活动且未过账的草稿允许修改或删除。");
        return header;
    }

    private async Task<Core.Entities.MasterData.Customer> RequireCustomerAsync(Guid uid, bool requireActive)
    {
        var customer = await customers.GetByIdAsync(uid);
        if (customer is null || customer.IsDeleted)
            throw new PackingListException(PackingListErrorCodes.InvalidDraft, "客户不存在或已删除。", "Header.CustomerId");
        if (requireActive && !customer.IsActive)
            Fail(PackingListErrorCodes.InvalidDraft, "客户已停用，不能建立新的业务引用。", "Header.CustomerId");
        return customer;
    }

    private async Task<PackingListDraftCommandResult> ResultAsync(Guid uid, bool alreadyApplied, CancellationToken token)
    {
        var detail = await repository.GetDetailAsync(uid, token)
            ?? throw new PackingListException(PackingListErrorCodes.OutcomeUnknown, "草稿已保存但无法重新读取完整结果。");
        return new PackingListDraftCommandResult { Uid = uid, AlreadyApplied = alreadyApplied, Detail = detail };
    }

    private static void ValidateLineSet(IReadOnlyList<PackingListLine> existing,
        IReadOnlyList<PackingListDraftLineInput> active, IReadOnlyList<PackingListDeletedLineInput> deleted)
    {
        var activeIds = active.Select(row => row.Uid).ToHashSet();
        var deletedIds = deleted.Select(row => row.Uid).ToHashSet();
        foreach (var row in existing)
        {
            var activeHit = activeIds.Contains(row.Uid);
            var deletedHit = deletedIds.Contains(row.Uid);
            if (activeHit == deletedHit)
                Fail(PackingListErrorCodes.LineSetMismatch, "每个存量行必须在有效行或删除行中恰好出现一次。", $"Lines[{row.Uid}]");
        }
        foreach (var input in active)
        {
            var old = existing.FirstOrDefault(row => row.Uid == input.Uid);
            if (old is null && input.ExpectedVersion != 0)
                Fail(PackingListErrorCodes.LineSetMismatch, "新增行 ExpectedVersion 必须为 0。", $"Lines[{input.Uid}].ExpectedVersion");
            if (old is not null && old.Version != input.ExpectedVersion)
                Fail(PackingListErrorCodes.VersionConflict, "装箱单行版本已变化。", $"Lines[{input.Uid}].ExpectedVersion");
        }
        foreach (var input in deleted)
        {
            var old = existing.FirstOrDefault(row => row.Uid == input.Uid);
            if (old is null) Fail(PackingListErrorCodes.LineSetMismatch, "待删除行不属于当前装箱单。", $"DeletedLines[{input.Uid}]");
            if (old!.Version != input.ExpectedVersion)
                Fail(PackingListErrorCodes.VersionConflict, "待删除行版本已变化。", $"DeletedLines[{input.Uid}].ExpectedVersion");
        }
    }

    private static void ValidateLineInputs(IReadOnlyList<PackingListDraftLineInput>? lines,
        IReadOnlyList<PackingListDeletedLineInput>? deleted)
    {
        if (lines is null || lines.Count is < 1 or > 500)
            Fail(PackingListErrorCodes.InvalidDraft, "装箱单必须包含 1 至 500 条有效行。", "Lines");
        deleted ??= Array.Empty<PackingListDeletedLineInput>();
        if (lines!.Any(row => row.Uid == Guid.Empty) || deleted.Any(row => row.Uid == Guid.Empty))
            Fail(PackingListErrorCodes.InvalidDraft, "头行身份必须为非空 Guid。", "Lines");
        if (lines.Select(row => row.Uid).Distinct().Count() != lines.Count
            || deleted.Select(row => row.Uid).Distinct().Count() != deleted.Count
            || lines.Select(row => row.Uid).Intersect(deleted.Select(row => row.Uid)).Any())
            Fail(PackingListErrorCodes.LineSetMismatch, "有效行与删除行存在重复或交叉身份。", "Lines");
        if (lines.Any(row => row.LineNumber <= 0)
            || lines.Select(row => row.LineNumber).Distinct().Count() != lines.Count)
            Fail(PackingListErrorCodes.InvalidDraft, "行号必须为唯一正整数。", "Lines.LineNumber");
        foreach (var row in lines)
        {
            var field = $"Lines[{row.Uid}]";
            if (row.GrossDemandId == Guid.Empty || row.ProductId == Guid.Empty)
                Fail(PackingListErrorCodes.InvalidDraft, "来源身份不能为空，标准行的产品身份也不能为空。", field);
            ValidateDecimal(row.Qty, false, field + ".Qty");
            if (row.PackingGroupId == Guid.Empty)
                Fail(PackingListErrorCodes.InvalidDraft, "拼箱标识不能是空 Guid。", field + ".PackingGroupId");
            if (row.PackingGroupId.HasValue) row.PcsPerPackage = row.Qty;
            if (row.NwPerPackage > row.GwPerPackage)
                Fail(PackingListErrorCodes.InvalidDraft, "单箱净重不能大于毛重。", field);
            if (row.PcsPerPackage.HasValue) ValidateDecimal(row.PcsPerPackage.Value, false, field + ".PcsPerPackage");
            ValidateOptionalNonNegative(row.NwPerPackage, field + ".NwPerPackage");
            ValidateOptionalNonNegative(row.GwPerPackage, field + ".GwPerPackage");
            ValidateOptionalNonNegative(row.CbmPerPackage, field + ".CbmPerPackage");
            CalculatePackageCount(row.Qty, row.PcsPerPackage);
        }
        // 组只是一只箱的包装归属；不能用某个成员覆盖其他成员的冲突事实。
        foreach (var group in lines.Where(x => x.PackingGroupId.HasValue).GroupBy(x => x.PackingGroupId))
        {
            var ordered = group.OrderBy(x => x.LineNumber).ToArray();
            if (ordered[^1].LineNumber - ordered[0].LineNumber + 1 != ordered.Length)
                Fail(PackingListErrorCodes.InvalidDraft, "同一拼箱成员的行号必须连续。", "Lines.LineNumber");
            if (ordered.Select(x => (x.NwPerPackage, x.GwPerPackage, x.CbmPerPackage, Marks: Normalize(x.Marks))).Distinct().Count() != 1)
                Fail(PackingListErrorCodes.InvalidDraft, "同一拼箱的净重、毛重、体积及箱唛必须一致。", "Lines.PackingGroupId");
        }
    }

    private static void ValidateHeader(PackingListDraftHeaderInput? header)
    {
        if (header is null) Fail(PackingListErrorCodes.InvalidDraft, "装箱单头不能为空。", "Header");
        if (header!.CustomerId == Guid.Empty) Fail(PackingListErrorCodes.InvalidDraft, "客户标识不能为空。", "Header.CustomerId");
        if (header.InvoiceDate == default) Fail(PackingListErrorCodes.InvalidDraft, "单据日期无效。", "Header.InvoiceDate");
        Length(header.CustomerNameSnapshot, 300, "Header.CustomerNameSnapshot");
        Length(header.CustomerAddressSnapshot, 2000, "Header.CustomerAddressSnapshot");
        Length(header.AttentionSnapshot, 2000, "Header.AttentionSnapshot");
        Length(header.ContainerNo, 100, "Header.ContainerNo");
        Length(header.ShippingLocationSnapshot, 200, "Header.ShippingLocationSnapshot");
        Length(header.SealNo, 100, "Header.SealNo");
        Length(header.Note, 2000, "Header.Note");
    }

    private static void ApplyHeader(PackingListHead target, PackingListDraftHeaderInput input)
    {
        target.CustomerId = input.CustomerId;
        target.CustomerNameSnapshot = Clean(input.CustomerNameSnapshot, 300);
        target.CustomerAddressSnapshot = Clean(input.CustomerAddressSnapshot, 2000);
        target.AttentionSnapshot = Clean(input.AttentionSnapshot, 2000);
        target.InvoiceDate = input.InvoiceDate;
        target.ContainerNo = Clean(input.ContainerNo, 100);
        target.ShippingLocationSnapshot = Clean(input.ShippingLocationSnapshot, 200);
        target.SealNo = Clean(input.SealNo, 100);
        target.Note = Clean(input.Note, 2000);
        target.Etd = input.Etd;
        target.Eta = input.Eta;
    }

    private static string Fingerprint(string operation, Guid uid, int version, PackingListDraftHeaderInput? header,
        IEnumerable<PackingListDraftLineInput> lines, IEnumerable<PackingListDeletedLineInput> deleted)
    {
        var canonical = new
        {
            Operation = operation,
            Uid = uid,
            ExpectedVersion = version,
            Header = header is null ? null : new
            {
                header.CustomerId,
                CustomerNameSnapshot = Normalize(header.CustomerNameSnapshot),
                CustomerAddressSnapshot = Normalize(header.CustomerAddressSnapshot),
                AttentionSnapshot = Normalize(header.AttentionSnapshot),
                header.InvoiceDate,
                ContainerNo = Normalize(header.ContainerNo),
                SealNo = Normalize(header.SealNo),
                ShippingLocationSnapshot = Normalize(header.ShippingLocationSnapshot),
                Note = Normalize(header.Note),
                header.Etd,
                header.Eta
            },
            Lines = lines.OrderBy(row => row.Uid).Select(row => new
            {
                row.Uid, row.ExpectedVersion, row.LineNumber, row.PackingGroupId, row.GrossDemandId, row.ProductId, row.Qty,
                row.PcsPerPackage, row.NwPerPackage, row.GwPerPackage, row.CbmPerPackage,
                InvoiceNo = Normalize(row.InvoiceNo), Marks = Normalize(row.Marks), Note = Normalize(row.Note)
            }).ToArray(),
            DeletedLines = deleted.OrderBy(row => row.Uid).Select(row => new { row.Uid, row.ExpectedVersion }).ToArray()
        };
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(canonical)));
        return Convert.ToHexString(bytes);
    }

    /// <summary>统一草稿入口的授权、请求验证与耗时日志，保留装箱单原有结构错误代码。</summary>
    private async Task<ServiceResult<PackingListDraftCommandResult>> RunDraftPipelineAsync<TRequest>(TRequest? request,
        Guid? commandId, string commandType,
        Func<CancellationToken, Task<ServiceResult<CommandExecutionResult<PackingListDraftCommandResult>>>> execute,
        CancellationToken token)
    {
        try
        {
            return await pipeline.ExecuteAsync<TRequest, PackingListDraftCommandResult>(request, commandId,
                commandType, AuthorizeDraftAsync, execute, token);
        }
        catch (CommandValidationException ex)
        {
            return ServiceResult<PackingListDraftCommandResult>.Fail(PackingListErrorCodes.InvalidDraft, ex.Message, ex.Field);
        }
    }

    /// <summary>草稿与查询沿用出货模块访问权限；授权必须早于回执重放。</summary>
    private async Task<ServiceResult<PackingListDraftCommandResult>?> AuthorizeDraftAsync(CancellationToken token)
    {
        if (!currentUser.IsAuthenticated || currentUser.UserId is not { } actor
            || !await permissions.HasPermissionAsync(actor, PermissionKeys.ModuleShippingAccess, token))
            return ServiceResult<PackingListDraftCommandResult>.Fail(ServiceErrorCodes.Forbidden, "当前账号没有出货模块访问权限。");
        return null;
    }

    private async Task<T> RunAuthorizedAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        var userId = currentUser.UserId;
        if (!currentUser.IsAuthenticated || !userId.HasValue
            || !await permissions.HasPermissionAsync(userId.Value, PermissionKeys.ModuleShippingAccess, token))
            throw new PackingListException(ServiceErrorCodes.Forbidden, "当前账号没有出货模块访问权限。");
        return await action();
    }

    private async Task<ServiceResult<T>> RunAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        try { return ServiceResult<T>.Ok(await RunAuthorizedAsync(action, token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (PackingListException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (BusinessCommandException ex) { return ServiceResult<T>.Fail(ex.Code, ex.Message, ex.Field); }
        catch (DBConcurrencyException ex) { return ServiceResult<T>.Fail(PackingListErrorCodes.VersionConflict, ex.Message); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "装箱单操作失败，未提交变更。");
            return ServiceResult<T>.Fail(ServiceErrorCodes.Unexpected, "装箱单操作失败，未提交变更。");
        }
    }

    private async Task RequireSchemaAsync(CancellationToken token)
    {
        if (!await repository.IsSchemaReadyAsync(token))
            Fail(PackingListErrorCodes.DeliveryUnavailable, "PackingList 新表或幂等索引尚未完整部署。");
    }

    private static void ValidateIdentity(Guid uid, Guid commandId, int expectedVersion)
    {
        if (uid == Guid.Empty) Fail(PackingListErrorCodes.InvalidDraft, "装箱单标识不能为空。", "Uid");
        if (commandId == Guid.Empty) Fail(PackingListErrorCodes.InvalidDraft, "CommandId 不能为空。", "CommandId");
        if (expectedVersion <= 0) Fail(PackingListErrorCodes.InvalidDraft, "ExpectedVersion 必须大于零。", "ExpectedVersion");
    }

    private static void ValidateDecimal(decimal value, bool allowZero, string field)
    {
        if (value < 0 || (!allowZero && value == 0) || value > MaxDecimal || decimal.Round(value, 4) != value)
            Fail(PackingListErrorCodes.InvalidDraft, "数值必须为最多四位小数的有效正数。", field);
    }

    private static decimal? Multiply(decimal? count, decimal? value)
    {
        if (!count.HasValue || !value.HasValue) return null;
        try
        {
            var result = decimal.Round(count.Value * value.Value, 4, MidpointRounding.AwayFromZero);
            if (result > MaxDecimal) Fail(PackingListErrorCodes.InvalidDraft, "包装小计超出允许范围。");
            return result;
        }
        catch (OverflowException) { Fail(PackingListErrorCodes.InvalidDraft, "包装小计超出允许范围。"); return null; }
    }

    /// <summary>服务端重算四位箱数，不信任客户端派生值，也不对尾箱向上取整。</summary>
    private static decimal? CalculatePackageCount(decimal qty, decimal? pcs)
    {
        if (!pcs.HasValue) return null;
        try
        {
            var value = decimal.Round(qty / pcs.Value, 4, MidpointRounding.AwayFromZero);
            ValidateDecimal(value, true, "PackageCount");
            return value;
        }
        catch (OverflowException) { Fail(PackingListErrorCodes.InvalidDraft, "箱数超出允许范围。", "PackageCount"); return null; }
    }

    private static void ValidateOptionalNonNegative(decimal? value, string field)
    {
        if (value.HasValue && (value.Value < 0 || value.Value > MaxDecimal || decimal.Round(value.Value, 4) != value.Value))
            Fail(PackingListErrorCodes.SourceChanged, "默认包装数值必须为最多四位小数的非负数。", field);
    }

    private static string RequiredSnapshot(string? value, string message, Guid uid, string field, int maxLength = 100)
    {
        var result = Clean(value, maxLength);
        if (string.IsNullOrWhiteSpace(result)) Fail(PackingListErrorCodes.SourceChanged, message, $"Lines[{uid}].{field}");
        return result!;
    }

    private static string? Clean(string? value, int maxLength)
    {
        var result = value?.Trim();
        if (string.IsNullOrEmpty(result)) return null;
        if (result.Length > maxLength) Fail(PackingListErrorCodes.InvalidDraft, $"文本长度不能超过 {maxLength} 个字符。");
        return result;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Length(string? value, int maxLength, string field)
    {
        if (value?.Trim().Length > maxLength)
            Fail(PackingListErrorCodes.InvalidDraft, $"文本长度不能超过 {maxLength} 个字符。", field);
    }

    [DoesNotReturn]
    private static void Fail(string code, string message, string? field = null) =>
        throw new PackingListException(code, message, field);
}



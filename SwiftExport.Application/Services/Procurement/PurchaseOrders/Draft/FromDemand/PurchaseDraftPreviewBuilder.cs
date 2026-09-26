using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseDrafts;
using SwiftExport.Contracts.SysEnums;
using SwiftExport.Contracts.SysEnums.StaticEnums;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SwiftExport.Application.Services.Procurement.PurchaseOrders.Draft.FromDemand;

/// <summary>
/// 采购草稿预览纯计算器。调用方负责先读取活动物料需求、已承接量、实际供应商选择和采购资料事实。
/// 本类型不访问数据库，也不创建采购单或 DemandExecutionLink。
/// </summary>
public sealed class PurchaseDraftPreviewBuilder
{
    /// <summary>按来源、实际采购供应商和兼容快照生成可写入的采购草稿预览。</summary>
    public PurchaseDraftPreviewResponse Build(PurchaseDraftPreviewBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var candidates = request.Candidates
            .Where(x => x is not null)
            .OrderBy(x => x.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceUid)
            .ThenBy(x => x.SourceLineNumber ?? int.MaxValue)
            .ThenBy(x => x.NetDemandId)
            .ToList();

        var response = new PurchaseDraftPreviewResponse
        {
            ExecutionRequestId = request.ExecutionRequestId,
            EntryMode = request.EntryMode,
            LineMode = request.LineMode,
            Currency = string.Empty
        };

        ValidateOneClickScope(request, candidates);

        var selected = SelectCandidates(request.EntryMode, candidates, response);
        var usable = new List<SelectedPurchaseDraftCandidate>();

        foreach (var selection in selected)
        {
            ValidateAndCollect(selection, response, usable);
        }

        AddSourceConsistencyWarnings(selected, response);
        AddRelatedCustomerSuppliedWarnings(candidates, usable, response);
        BuildSourceGroups(request, candidates, selected, usable, response);

        var currencies = response.SourceGroups.SelectMany(x => x.Documents)
            .Select(x => x.Header.Currency?.Trim() ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        response.Currency = currencies.Count == 1 ? currencies[0] : string.Empty;

        response.HasBlockers = response.Warnings.Any(x => x.IsBlocker);
        response.RequiresConfirmation = response.Warnings.Any(x => x.RequiresConfirmation);
        response.PreviewToken = BuildPreviewToken(request, selected, response.Warnings);
        return response;
    }

    private static List<SelectedPurchaseDraftCandidate> SelectCandidates(
        PurchaseDraftEntryModeEnum entryMode,
        IReadOnlyList<PurchaseDraftPreviewCandidate> candidates,
        PurchaseDraftPreviewResponse response)
    {
        var selected = new List<SelectedPurchaseDraftCandidate>();

        foreach (var candidate in candidates)
        {
            var isInScope = entryMode == PurchaseDraftEntryModeEnum.SourceOneClick
                ? candidate.RemainingQty > 0
                : candidate.IsSelected;

            if (!isInScope)
            {
                continue;
            }

            if (candidate.ExecutionType != DemandExecutionTypeEnum.Purchase)
            {
                response.SkippedItems.Add(new PurchaseDraftSkippedItemDto
                {
                    NetDemandId = candidate.NetDemandId,
                    ExecutionType = candidate.ExecutionType,
                    ReasonCode = PurchaseDraftWarningCodes.ExecutionTypeNotPurchase,
                    Message = "当前计划不是采购执行方式，本次不处理。"
                });
                continue;
            }

            var purchaseQty = entryMode == PurchaseDraftEntryModeEnum.SourceOneClick
                ? candidate.RemainingQty
                : candidate.RequestedPurchaseQty ?? candidate.RemainingQty;

            selected.Add(new SelectedPurchaseDraftCandidate(candidate, purchaseQty));
        }

        return selected;
    }

    private static void ValidateAndCollect(
        SelectedPurchaseDraftCandidate selection,
        PurchaseDraftPreviewResponse response,
        ICollection<SelectedPurchaseDraftCandidate> usable)
    {
        var candidate = selection.Candidate;
        var valid = true;

        if (!candidate.IsPlanningFactValid)
        {
            AddWarning(response, candidate, PurchaseDraftWarningCodes.CorruptedPlanningState,
                "物料需求或来源关系不完整；请刷新候选，若仍存在请移除选择并联系计划人员核对。",
                isBlocker: true, requiresConfirmation: false);
            valid = false;
        }

        if (selection.PurchaseQty <= 0 || selection.PurchaseQty > candidate.RemainingQty)
        {
            AddWarning(response, candidate, PurchaseDraftWarningCodes.InvalidPurchaseQuantity,
                $"采购数量必须大于 0 且不超过剩余量 {candidate.RemainingQty.ToString("0.####", CultureInfo.InvariantCulture)}；请修正数量后重新预览。",
                isBlocker: true, requiresConfirmation: false);
            valid = false;
        }

        if (!candidate.PurchaseSupplierId.HasValue || candidate.PurchaseSupplierId.Value == Guid.Empty)
        {
            AddWarning(response, candidate, PurchaseDraftWarningCodes.PurchaseSupplierRequired,
                "实际采购供应商尚未选择；请在本次计划中补选后重新预览。",
                isBlocker: true, requiresConfirmation: false);
            valid = false;
        }
        else if (!candidate.PurchaseSupplierIsActive)
        {
            AddWarning(response, candidate, PurchaseDraftWarningCodes.PurchaseSupplierInvalid,
                "实际采购供应商已失效；请在本次计划中重新选择有效供应商。",
                isBlocker: true, requiresConfirmation: false);
            valid = false;
        }

        if (candidate.IsCustomerSupplied
            && (!candidate.DeliveryTargetNetDemandId.HasValue
                || candidate.DeliveryTargetNetDemandId.Value == Guid.Empty
                || !candidate.DeliveryTargetSupplierId.HasValue
                || candidate.DeliveryTargetSupplierId.Value == Guid.Empty
                || !candidate.DeliveryTargetIsValid))
        {
            AddWarning(response, candidate, PurchaseDraftWarningCodes.DeliveryTargetRequired,
                "客供件实际送达方或父件关系无效；请明确选择送达方，若关系失效请刷新或移除选择。",
                isBlocker: true, requiresConfirmation: false);
            valid = false;
        }

        if (!candidate.HasSupplierProduct)
        {
            AddWarning(response, candidate, PurchaseDraftWarningCodes.PurchaseDataIncomplete,
                "供应商产品资料未建立；允许生成草稿，创建后可在采购详情补齐资料。",
                isBlocker: false, requiresConfirmation: false);
        }

        if (valid)
        {
            usable.Add(selection);
        }
    }

    private static void AddRelatedCustomerSuppliedWarnings(
        IReadOnlyList<PurchaseDraftPreviewCandidate> candidates,
        IReadOnlyList<SelectedPurchaseDraftCandidate> selected,
        PurchaseDraftPreviewResponse response)
    {
        var selectedQty = selected
            .GroupBy(x => x.Candidate.NetDemandId)
            .ToDictionary(x => x.Key, x => x.Sum(y => y.PurchaseQty));

        foreach (var parent in selected.Where(x => !x.Candidate.IsCustomerSupplied))
        {
            var remainingChildren = candidates
                .Where(x => x.IsCustomerSupplied
                    && x.ExecutionType == DemandExecutionTypeEnum.Purchase
                    && x.DeliveryTargetNetDemandId == parent.Candidate.NetDemandId)
                .Where(x => x.RemainingQty - selectedQty.GetValueOrDefault(x.NetDemandId) > 0)
                .OrderBy(x => x.SourceLineNumber ?? int.MaxValue)
                .ThenBy(x => x.NetDemandId)
                .ToList();

            if (remainingChildren.Count == 0)
            {
                continue;
            }

            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.RelatedCustomerSuppliedRemaining,
                Message = "当前父件转采购后，仍有关联客供件存在未转采购数量。确认不影响本次父件采购后方可继续。",
                IsBlocker = false,
                RequiresConfirmation = true,
                SourceType = parent.Candidate.SourceType,
                SourceUid = parent.Candidate.SourceUid,
                SupplierId = parent.Candidate.PurchaseSupplierId,
                NetDemandIds = new[] { parent.Candidate.NetDemandId }
                    .Concat(remainingChildren.Select(x => x.NetDemandId))
                    .ToList(),
                AffectedObjects = new[]
                    {
                        MapAffectedObject(
                            parent.Candidate,
                            isSelectedForPurchase: true,
                            remainingAfterSelectionQty: Math.Max(0, parent.Candidate.RemainingQty - selectedQty.GetValueOrDefault(parent.Candidate.NetDemandId)))
                    }
                    .Concat(remainingChildren.Select(child => MapAffectedObject(
                        child,
                        selectedQty.ContainsKey(child.NetDemandId),
                        Math.Max(0, child.RemainingQty - selectedQty.GetValueOrDefault(child.NetDemandId)))))
                    .ToList()
            });
        }
    }

    private static void AddSourceConsistencyWarnings(
        IReadOnlyList<SelectedPurchaseDraftCandidate> selected,
        PurchaseDraftPreviewResponse response)
    {
        foreach (var source in selected.GroupBy(x => new
                 {
                     SourceType = x.Candidate.SourceType?.Trim().ToUpperInvariant() ?? string.Empty,
                     x.Candidate.SourceUid
                 }))
        {
            var first = source.First().Candidate;
            if (source.Select(x => x.Candidate.CustomerId).Distinct().Count() > 1)
            {
                response.Warnings.Add(new PurchaseDraftWarningDto
                {
                    Code = PurchaseDraftWarningCodes.CorruptedPlanningState,
                    Message = "同一来源存在不同客户身份，不能确定采购单头默认配置。",
                    IsBlocker = true,
                    RequiresConfirmation = false,
                    SourceType = first.SourceType,
                    SourceUid = first.SourceUid,
                    NetDemandIds = source.Select(x => x.Candidate.NetDemandId).ToList(),
                    AffectedObjects = source.Select(x => MapAffectedObject(
                        x.Candidate,
                        isSelectedForPurchase: true,
                        remainingAfterSelectionQty: Math.Max(0, x.Candidate.RemainingQty - x.PurchaseQty))).ToList()
                });
            }

            if (source.Select(x => x.Candidate.SourceNo?.Trim() ?? string.Empty)
                .Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1)
            {
                continue;
            }

            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.CorruptedPlanningState,
                Message = "同一来源 Uid 存在不同来源单号快照，不能生成采购草稿。",
                IsBlocker = true,
                RequiresConfirmation = false,
                SourceType = first.SourceType,
                SourceUid = first.SourceUid,
                NetDemandIds = source.Select(x => x.Candidate.NetDemandId).ToList(),
                AffectedObjects = source.Select(x => MapAffectedObject(
                    x.Candidate,
                    isSelectedForPurchase: true,
                    remainingAfterSelectionQty: Math.Max(0, x.Candidate.RemainingQty - x.PurchaseQty))).ToList()
            });
        }
    }

    private static void BuildSourceGroups(
        PurchaseDraftPreviewBuildRequest request,
        IReadOnlyList<PurchaseDraftPreviewCandidate> candidates,
        IReadOnlyList<SelectedPurchaseDraftCandidate> selected,
        IReadOnlyList<SelectedPurchaseDraftCandidate> usable,
        PurchaseDraftPreviewResponse response)
    {
        var sourceKeys = usable
            .Select(x => new SourceKey(x.Candidate.SourceType, x.Candidate.SourceUid, x.Candidate.SourceNo))
            .Concat(response.Warnings
                .Where(x => x.SourceUid.HasValue)
                .Select(x => new SourceKey(x.SourceType, x.SourceUid!.Value,
                    candidates.FirstOrDefault(y => SourceEquals(y, x.SourceType, x.SourceUid.Value))?.SourceNo ?? string.Empty)))
            .GroupBy(x => new { SourceType = x.SourceType?.Trim().ToUpperInvariant() ?? string.Empty, x.SourceUid })
            .Select(x => x.First())
            .OrderBy(x => x.SourceType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(x => x.SourceUid)
            .ToList();

        foreach (var sourceKey in sourceKeys)
        {
            var sourceSelections = usable
                .Where(x => SourceEquals(x.Candidate, sourceKey.SourceType, sourceKey.SourceUid))
                .ToList();
            var sourceTokenSelections = selected
                .Where(x => SourceEquals(x.Candidate, sourceKey.SourceType, sourceKey.SourceUid))
                .ToList();
            var sourceWarnings = response.Warnings
                .Where(x => x.SourceUid == sourceKey.SourceUid
                    && string.Equals(x.SourceType, sourceKey.SourceType, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var sourceGroup = new PurchaseDraftPreviewSourceGroupDto
            {
                SourceType = sourceKey.SourceType,
                SourceUid = sourceKey.SourceUid,
                SourceNo = sourceKey.SourceNo,
                CanCreate = sourceWarnings.All(x => !x.IsBlocker),
                Warnings = sourceWarnings
            };

            foreach (var supplierGroup in sourceSelections
                         .Where(x => x.Candidate.PurchaseSupplierId.HasValue)
                         .GroupBy(x => x.Candidate.PurchaseSupplierId!.Value)
                         .OrderBy(x => x.Key))
            {
                sourceGroup.Documents.Add(BuildDocument(request, sourceKey, supplierGroup.Key, supplierGroup.ToList(), response));
            }

            if (request.LineMode == PurchaseDraftLineModeEnum.MergeCompatibleLines)
            {
                AddSupplierSplitReasons(sourceGroup);
            }

            sourceGroup.Warnings = response.Warnings
                .Where(x => x.SourceUid == sourceKey.SourceUid
                    && string.Equals(x.SourceType, sourceKey.SourceType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            sourceGroup.CanCreate = sourceGroup.Warnings.All(x => !x.IsBlocker);
            sourceGroup.SourcePreviewToken = BuildPreviewToken(request, sourceTokenSelections, sourceGroup.Warnings, sourceKey);
            sourceGroup.DocumentCount = sourceGroup.Documents.Count;
            response.SourceGroups.Add(sourceGroup);
        }
    }

    private static PurchaseDraftPreviewDocumentDto BuildDocument(
        PurchaseDraftPreviewBuildRequest request,
        SourceKey sourceKey,
        Guid supplierId,
        IReadOnlyList<SelectedPurchaseDraftCandidate> selections,
        PurchaseDraftPreviewResponse response)
    {
        var first = selections[0].Candidate;
        var target = request.EntryMode == PurchaseDraftEntryModeEnum.SourceOneClick
            ? null
            : request.Targets.FirstOrDefault(x => SourceEquals(x, sourceKey.SourceType, sourceKey.SourceUid)
                && x.SupplierId == supplierId);

        var document = new PurchaseDraftPreviewDocumentDto
        {
            SourceType = sourceKey.SourceType,
            SourceUid = sourceKey.SourceUid,
            SourceNo = sourceKey.SourceNo,
            CustomerId = first.CustomerId,
            CustomerCode = first.CustomerCode,
            CustomerName = first.CustomerName,
            SupplierId = supplierId,
            SupplierCode = first.PurchaseSupplierCode,
            SupplierName = first.PurchaseSupplierName,
            Currency = string.Empty,
            TargetMode = target?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew,
            ExistingPurchaseOrderId = target?.ExistingPurchaseOrderId,
            ExpectedPurchaseOrderVersion = target?.ExpectedPurchaseOrderVersion,
            SourceLineCount = selections.Count,
            TotalQty = selections.Sum(x => x.PurchaseQty),
            EarliestPlannedDeliveryDate = MinDate(selections.Select(x => x.Candidate.PlannedDeliveryDate))
        };

        if (request.LineMode == PurchaseDraftLineModeEnum.KeepSourceLines)
        {
            document.Lines = selections
                .OrderBy(x => x.Candidate.SourceLineNumber ?? int.MaxValue)
                .ThenBy(x => x.Candidate.NetDemandId)
                .Select(x => BuildLine(new[] { x }, response))
                .ToList();
        }
        else
        {
            var mergedGroups = selections
                .GroupBy(x => new MergeKey(x.Candidate))
                .OrderBy(x => x.Min(y => y.Candidate.SourceLineNumber ?? int.MaxValue))
                .ThenBy(x => x.Min(y => y.Candidate.NetDemandId))
                .ToList();
            document.Lines = mergedGroups
                .Select(group => BuildLine(group.ToList(), response))
                .ToList();

            for (var index = 0; index < mergedGroups.Count; index++)
            {
                var current = mergedGroups[index];
                var reasons = mergedGroups
                    .Where(other => !ReferenceEquals(other, current)
                        && string.Equals(other.Key.Sku, current.Key.Sku, StringComparison.Ordinal))
                    .SelectMany(other => current.Key.DescribeDifferences(other.Key))
                    .Distinct(StringComparer.Ordinal)
                    .ToList();
                document.Lines[index].MergeDifferenceReason = string.Join("；", reasons);
            }
        }

        document.PreviewLineCount = document.Lines.Count;
        var lineUntaxedTotal = document.Lines.Sum(line => (line.UnitPrice ?? 0m) * line.PurchaseQty);
        var lineTaxIncludedTotal = document.Lines.Sum(line => (line.TaxPrice ?? 0m) * line.PurchaseQty);
        var appendCandidate = target?.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting
            ? request.SafeAppendCandidates.SingleOrDefault(x => x.PurchaseOrderId == target.ExistingPurchaseOrderId)
            : null;
        var newHeader = request.Headers.SingleOrDefault(x => SourceEquals(x, sourceKey.SourceType, sourceKey.SourceUid)
            && x.SupplierId == supplierId)?.Header ?? new PurchaseDraftPreviewHeaderDto();
        document.Header = CloneHeader(appendCandidate?.Header ?? newHeader);
        document.Currency = document.Header.Currency;
        document.Header.UntaxedTotalAmount = (appendCandidate?.UntaxedTotalAmount ?? 0m) + lineUntaxedTotal;
        document.Header.TaxIncludedTotalAmount = (appendCandidate?.TaxIncludedTotalAmount ?? 0m) + lineTaxIncludedTotal;
        document.Header.ExpectedDate = MinDate(new[] { appendCandidate?.Header.ExpectedDate, document.EarliestPlannedDeliveryDate });
        document.SafeAppendCandidates = request.SafeAppendCandidates
            .Where(x => SourceEquals(x, sourceKey.SourceType, sourceKey.SourceUid) && x.SupplierId == supplierId)
            .OrderBy(x => x.OrderNo, StringComparer.OrdinalIgnoreCase)
            .Select(x => new SafePurchaseDraftCandidateDto
            {
                PurchaseOrderId = x.PurchaseOrderId,
                Version = x.Version,
                OrderNo = x.OrderNo,
                SourceType = x.SourceType,
                SourceUid = x.SourceUid,
                SupplierId = x.SupplierId,
                UntaxedTotalAmount = x.UntaxedTotalAmount,
                TaxIncludedTotalAmount = x.TaxIncludedTotalAmount
            })
            .ToList();
        return document;
    }

    private static PurchaseDraftPreviewLineDto BuildLine(
        IReadOnlyList<SelectedPurchaseDraftCandidate> selections,
        PurchaseDraftPreviewResponse response)
    {
        var first = selections[0].Candidate;
        var dates = selections
            .Select(x => x.Candidate.PlannedDeliveryDate?.Date)
            .Distinct()
            .OrderBy(x => x.HasValue ? 0 : 1)
            .ThenBy(x => x)
            .ToList();

        if (dates.Count > 1)
        {
            response.Warnings.Add(new PurchaseDraftWarningDto
            {
                Code = PurchaseDraftWarningCodes.DifferentPlannedDeliveryDates,
                Message = "合并行包含不同计划交期；采购交期已取最早日期，确认后方可继续。",
                IsBlocker = false,
                RequiresConfirmation = true,
                SourceType = first.SourceType,
                SourceUid = first.SourceUid,
                SupplierId = first.PurchaseSupplierId,
                NetDemandIds = selections.Select(x => x.Candidate.NetDemandId).ToList(),
                AffectedObjects = selections.Select(x => MapAffectedObject(
                    x.Candidate,
                    isSelectedForPurchase: true,
                    remainingAfterSelectionQty: Math.Max(0, x.Candidate.RemainingQty - x.PurchaseQty))).ToList()
            });
        }

        return new PurchaseDraftPreviewLineDto
        {
            ProductId = first.ProductId,
            Sku = first.Sku,
            LinkSku = first.LinkSku,
            CustomerSku = first.CustomerSku,
            Description = first.Description,
            Color = first.Color,
            Specification = first.Specification,
            ImagePath = first.ImagePath,
            MaterialType = first.MaterialType,
            IsCustomerSupplied = first.IsCustomerSupplied,
            Unit = first.Unit,
            QuantityScale = first.QuantityScale,
            SupplierSku = first.SupplierSku,
            UnitPrice = first.UnitPrice,
            TaxPrice = first.TaxPrice,
            PurchaseQty = selections.Sum(x => x.PurchaseQty),
            PlannedDeliveryDate = MinDate(dates),
            DeliveryTargetSupplierId = first.DeliveryTargetSupplierId,
            DeliveryTargetSupplierCode = first.DeliveryTargetSupplierCode,
            DeliveryTargetSupplierName = first.DeliveryTargetSupplierName,
            PurchaseDataIncomplete = selections.Any(x => !x.Candidate.HasSupplierProduct),
            SourcePlannedDeliveryDates = dates,
            Contributions = selections
                .OrderBy(x => x.Candidate.SourceLineNumber ?? int.MaxValue)
                .ThenBy(x => x.Candidate.NetDemandId)
                .Select(x => new PurchaseDraftSourceContributionDto
                {
                    CommandItemId = x.Candidate.CommandItemId,
                    NetDemandId = x.Candidate.NetDemandId,
                    NetDemandVersion = x.Candidate.NetDemandVersion,
                    SupplierId = x.Candidate.PurchaseSupplierId!.Value,
                    DeliveryTargetSupplierId = x.Candidate.DeliveryTargetSupplierId,
                    SourceType = x.Candidate.SourceType,
                    SourceNo = x.Candidate.SourceNo,
                    SourceLineId = x.Candidate.SourceLineId,
                    SourceLineNumber = x.Candidate.SourceLineNumber,
                    Sku = x.Candidate.Sku,
                    LinkSku = x.Candidate.LinkSku,
                    ImagePath = x.Candidate.ImagePath ?? string.Empty,
                    ExpectedRemainingQty = x.Candidate.RemainingQty,
                    PurchaseQty = x.PurchaseQty,
                    DemandRequiredDate = x.Candidate.DemandRequiredDate,
                    PlannedDeliveryDate = x.Candidate.PlannedDeliveryDate
                })
                .ToList()
        };
    }

    private static void AddWarning(
        PurchaseDraftPreviewResponse response,
        PurchaseDraftPreviewCandidate candidate,
        string code,
        string message,
        bool isBlocker,
        bool requiresConfirmation)
    {
        response.Warnings.Add(new PurchaseDraftWarningDto
        {
            Code = code,
            Message = message,
            IsBlocker = isBlocker,
            RequiresConfirmation = requiresConfirmation,
            SourceType = candidate.SourceType,
            SourceUid = candidate.SourceUid,
            SupplierId = candidate.PurchaseSupplierId,
            NetDemandIds = new List<Guid> { candidate.NetDemandId },
            AffectedObjects = new List<PurchaseDraftWarningAffectedObjectDto>
            {
                MapAffectedObject(
                    candidate,
                    isSelectedForPurchase: true,
                    remainingAfterSelectionQty: Math.Max(0, candidate.RemainingQty - (candidate.RequestedPurchaseQty ?? 0)))
            }
        });
    }

    /// <summary>把服务端候选事实收窄为警告辨认摘要；只描述确认范围，不改变采购选择或数量。</summary>
    private static PurchaseDraftWarningAffectedObjectDto MapAffectedObject(
        PurchaseDraftPreviewCandidate candidate,
        bool isSelectedForPurchase,
        decimal remainingAfterSelectionQty) => new()
    {
        NetDemandId = candidate.NetDemandId,
        SourceNo = candidate.SourceNo,
        SourceLineNumber = candidate.SourceLineNumber,
        Sku = candidate.Sku,
        LinkSku = candidate.LinkSku,
        IsIncludedInCurrentPurchase = isSelectedForPurchase,
        RemainingAfterSelectionQty = remainingAfterSelectionQty
    };

    private static string BuildPreviewToken(
        PurchaseDraftPreviewBuildRequest request,
        IReadOnlyList<SelectedPurchaseDraftCandidate> selected,
        IReadOnlyList<PurchaseDraftWarningDto> warnings,
        SourceKey? sourceKey = null)
    {
        var text = new StringBuilder();
        text.Append(request.ExecutionRequestId.ToString("N"))
            .Append('|').Append((int)request.EntryMode)
            .Append('|').Append((int)request.LineMode);

        foreach (var item in selected
                     .OrderBy(x => x.Candidate.SourceType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.Candidate.SourceUid)
                     .ThenBy(x => x.Candidate.PurchaseSupplierId)
                     .ThenBy(x => x.Candidate.NetDemandId))
        {
            var candidate = item.Candidate;
            text.Append("\nI|").Append(candidate.CommandItemId.ToString("N"))
                .Append('|').Append(candidate.NetDemandId.ToString("N"))
                .Append('|').Append(candidate.NetDemandVersion)
                .Append('|').Append(candidate.SourceType?.Trim().ToUpperInvariant())
                .Append('|').Append(candidate.SourceUid.ToString("N"))
                .Append('|').Append(candidate.SourceNo?.Trim())
                .Append('|').Append(candidate.SourceLineId.ToString("N"))
                .Append('|').Append(candidate.SourceLineNumber)
                .Append('|').Append(candidate.CustomerId?.ToString("N"))
                .Append('|').Append(candidate.CustomerCode?.Trim())
                .Append('|').Append(candidate.CustomerName?.Trim())
                .Append('|').Append((int)candidate.ExecutionType)
                .Append('|').Append(candidate.IsCustomerSupplied ? '1' : '0')
                .Append('|').Append(candidate.IsPlanningFactValid ? '1' : '0')
                .Append('|').Append(candidate.PurchaseSupplierId?.ToString("N"))
                .Append('|').Append(candidate.PurchaseSupplierIsActive ? '1' : '0')
                .Append('|').Append(candidate.PurchaseSupplierCode?.Trim())
                .Append('|').Append(candidate.PurchaseSupplierName?.Trim())
                .Append('|').Append(candidate.DeliveryTargetNetDemandId?.ToString("N"))
                .Append('|').Append(candidate.DeliveryTargetSupplierId?.ToString("N"))
                .Append('|').Append(candidate.DeliveryTargetIsValid ? '1' : '0')
                .Append('|').Append(candidate.DeliveryTargetSupplierCode?.Trim())
                .Append('|').Append(candidate.DeliveryTargetSupplierName?.Trim())
                .Append('|').Append(candidate.RemainingQty.ToString("0.####", CultureInfo.InvariantCulture))
                .Append('|').Append(item.PurchaseQty.ToString("0.####", CultureInfo.InvariantCulture))
                .Append('|').Append(candidate.DemandRequiredDate?.Ticks)
                .Append('|').Append(candidate.PlannedDeliveryDate?.Ticks)
                .Append('|').Append(candidate.ProductId?.ToString("N"))
                .Append('|').Append(candidate.Sku?.Trim().ToUpperInvariant())
                .Append('|').Append(candidate.LinkSku?.Trim().ToUpperInvariant())
                .Append('|').Append(candidate.CustomerSku?.Trim().ToUpperInvariant())
                .Append('|').Append(candidate.Description?.Trim())
                .Append('|').Append(candidate.Color?.Trim())
                .Append('|').Append(candidate.Specification?.Trim())
                .Append('|').Append(candidate.MaterialType?.Trim())
                .Append('|').Append(candidate.Unit?.Trim().ToUpperInvariant())
                .Append('|').Append(candidate.QuantityScale)
                .Append('|').Append(candidate.HasSupplierProduct ? '1' : '0')
                .Append('|').Append(candidate.SupplierSku?.Trim().ToUpperInvariant())
                .Append('|').Append(candidate.UnitPrice?.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(candidate.TaxPrice?.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var target in BuildCanonicalTargets(request, selected, sourceKey))
        {
            text.Append("\nT|").Append(target.SourceType)
                .Append('|').Append(target.SourceUid.ToString("N"))
                .Append('|').Append(target.SupplierId.ToString("N"))
                .Append('|').Append((int)target.TargetMode)
                .Append('|').Append(target.ExistingPurchaseOrderId?.ToString("N"))
                .Append('|').Append(target.ExpectedPurchaseOrderVersion);
        }

        foreach (var header in request.Headers
                     .Where(x => sourceKey is null || SourceEquals(x, sourceKey.SourceType, sourceKey.SourceUid))
                     .Where(x => request.Targets.FirstOrDefault(target => SourceEquals(target, x.SourceType, x.SourceUid)
                         && target.SupplierId == x.SupplierId)?.TargetMode != PurchaseDraftTargetModeEnum.AppendExisting)
                     .OrderBy(x => x.SourceType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.SourceUid)
                     .ThenBy(x => x.SupplierId))
        {
            AppendHeaderToken(text, "H", header.SourceType, header.SourceUid, header.SupplierId, header.Header);
        }

        foreach (var append in request.SafeAppendCandidates
                     .Where(x => sourceKey is null || SourceEquals(x, sourceKey.SourceType, sourceKey.SourceUid))
                     .Where(x => request.Targets.Any(target => SourceEquals(target, x.SourceType, x.SourceUid)
                         && target.SupplierId == x.SupplierId
                         && target.TargetMode == PurchaseDraftTargetModeEnum.AppendExisting
                         && target.ExistingPurchaseOrderId == x.PurchaseOrderId))
                     .OrderBy(x => x.PurchaseOrderId))
        {
            AppendHeaderToken(text, "A", append.SourceType, append.SourceUid, append.SupplierId, append.Header);
            text.Append('|').Append(append.PurchaseOrderId.ToString("N"))
                .Append('|').Append(append.Version)
                .Append('|').Append(append.UntaxedTotalAmount.ToString(CultureInfo.InvariantCulture))
                .Append('|').Append(append.TaxIncludedTotalAmount.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var warning in warnings
                     .OrderBy(x => x.Code, StringComparer.Ordinal)
                     .ThenBy(x => x.SourceType, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.SourceUid))
        {
            text.Append("\nW|").Append(warning.Code)
                .Append('|').Append(warning.IsBlocker ? '1' : '0')
                .Append('|').Append(warning.RequiresConfirmation ? '1' : '0')
                .Append('|').Append(string.Join(",", warning.NetDemandIds.OrderBy(x => x).Select(x => x.ToString("N"))));
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }

    /// <summary>把单头业务快照写入规范令牌文本；新单的系统生成单号和系统下单时间不参与比较。</summary>
    private static void AppendHeaderToken(
        StringBuilder text,
        string prefix,
        string sourceType,
        Guid sourceUid,
        Guid supplierId,
        PurchaseDraftPreviewHeaderDto header)
    {
        text.Append('\n').Append(prefix).Append('|').Append(sourceType?.Trim().ToUpperInvariant())
            .Append('|').Append(sourceUid.ToString("N"))
            .Append('|').Append(supplierId.ToString("N"))
            .Append('|').Append(header.OrderNo?.Trim())
            .Append('|').Append(header.CustomerPoNo?.Trim())
            .Append('|').Append(header.BoNo?.Trim())
            .Append('|').Append(header.Currency?.Trim().ToUpperInvariant())
            .Append('|').Append(header.DiscountRate?.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(header.DiscountAmount?.ToString(CultureInfo.InvariantCulture))
            .Append('|').Append(header.DiscountRemark?.Trim())
            .Append('|').Append(header.OrderDate?.Ticks)
            .Append('|').Append(header.ExpectedDate?.Ticks)
            .Append('|').Append(header.InletHoseType?.Trim())
            .Append('|').Append(header.AeratorType?.Trim())
            .Append('|').Append(header.PalletType?.Trim())
            .Append('|').Append(header.BoxType?.Trim())
            .Append('|').Append(header.LabelType?.Trim())
            .Append('|').Append(header.InstructionManualType?.Trim())
            .Append('|').Append(header.CartonType?.Trim())
            .Append('|').Append(header.LogoType?.Trim())
            .Append('|').Append(header.SalesPerson?.Trim())
            .Append('|').Append(header.PurchasePerson?.Trim())
            .Append('|').Append(header.Remark?.Trim());
    }

    private static IReadOnlyList<CanonicalTarget> BuildCanonicalTargets(
        PurchaseDraftPreviewBuildRequest request,
        IReadOnlyList<SelectedPurchaseDraftCandidate> selected,
        SourceKey? sourceKey)
    {
        var providedTargets = request.Targets
            .Where(x => sourceKey is null || SourceEquals(x, sourceKey.SourceType, sourceKey.SourceUid))
            .GroupBy(x => new
            {
                SourceType = x.SourceType?.Trim().ToUpperInvariant() ?? string.Empty,
                x.SourceUid,
                x.SupplierId
            })
            .ToDictionary(x => x.Key, x => x.First());

        return selected
            .Where(x => x.Candidate.ExecutionType == DemandExecutionTypeEnum.Purchase
                && x.Candidate.PurchaseSupplierId.HasValue
                && x.Candidate.PurchaseSupplierId.Value != Guid.Empty)
            .Select(x => new
            {
                SourceType = x.Candidate.SourceType?.Trim().ToUpperInvariant() ?? string.Empty,
                x.Candidate.SourceUid,
                SupplierId = x.Candidate.PurchaseSupplierId!.Value
            })
            .Distinct()
            .OrderBy(x => x.SourceType, StringComparer.Ordinal)
            .ThenBy(x => x.SourceUid)
            .ThenBy(x => x.SupplierId)
            .Select(x =>
            {
                providedTargets.TryGetValue(x, out var provided);
                var targetMode = provided?.TargetMode ?? PurchaseDraftTargetModeEnum.CreateNew;
                return new CanonicalTarget(
                    x.SourceType,
                    x.SourceUid,
                    x.SupplierId,
                    targetMode,
                    targetMode == PurchaseDraftTargetModeEnum.AppendExisting
                        ? provided?.ExistingPurchaseOrderId
                        : null,
                    targetMode == PurchaseDraftTargetModeEnum.AppendExisting
                        ? provided?.ExpectedPurchaseOrderVersion
                        : null);
            })
            .ToList();
    }

    private static void ValidateOneClickScope(
        PurchaseDraftPreviewBuildRequest request,
        IReadOnlyList<PurchaseDraftPreviewCandidate> candidates)
    {
        if (request.EntryMode != PurchaseDraftEntryModeEnum.SourceOneClick)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(request.SourceType) || !request.SourceUid.HasValue || request.SourceUid.Value == Guid.Empty)
        {
            throw new ArgumentException("来源一键预览必须提供唯一的 SourceType 和 SourceUid。", nameof(request));
        }

        if (candidates.Any(x => !SourceEquals(x, request.SourceType, request.SourceUid.Value)))
        {
            throw new ArgumentException("来源一键预览不能包含其他来源单的计划。", nameof(request));
        }
    }

    private static bool SourceEquals(PurchaseDraftPreviewCandidate candidate, string sourceType, Guid sourceUid)
        => candidate.SourceUid == sourceUid
           && string.Equals(candidate.SourceType, sourceType, StringComparison.OrdinalIgnoreCase);

    private static bool SourceEquals(PurchaseDraftPreviewBuildTarget target, string sourceType, Guid sourceUid)
        => target.SourceUid == sourceUid
           && string.Equals(target.SourceType, sourceType, StringComparison.OrdinalIgnoreCase);

    private static bool SourceEquals(PurchaseDraftSafeAppendCandidate candidate, string sourceType, Guid sourceUid)
        => candidate.SourceUid == sourceUid
           && string.Equals(candidate.SourceType, sourceType, StringComparison.OrdinalIgnoreCase);

    private static bool SourceEquals(PurchaseDraftPreviewHeaderFact candidate, string sourceType, Guid sourceUid)
        => candidate.SourceUid == sourceUid
           && string.Equals(candidate.SourceType, sourceType, StringComparison.OrdinalIgnoreCase);

    private static DateTime? MinDate(IEnumerable<DateTime?> dates)
    {
        var values = dates.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return values.Count == 0 ? null : values.Min();
    }

    /// <summary>复制单头后再补预览合计，避免纯计算器修改调用方提供的事实对象。</summary>
    private static PurchaseDraftPreviewHeaderDto CloneHeader(PurchaseDraftPreviewHeaderDto source) => new()
    {
        OrderNo = source.OrderNo,
        CustomerPoNo = source.CustomerPoNo,
        BoNo = source.BoNo,
        Currency = source.Currency,
        DiscountRate = source.DiscountRate,
        DiscountAmount = source.DiscountAmount,
        DiscountRemark = source.DiscountRemark,
        UntaxedTotalAmount = source.UntaxedTotalAmount,
        TaxIncludedTotalAmount = source.TaxIncludedTotalAmount,
        OrderDate = source.OrderDate,
        ExpectedDate = source.ExpectedDate,
        InletHoseType = source.InletHoseType,
        AeratorType = source.AeratorType,
        PalletType = source.PalletType,
        BoxType = source.BoxType,
        LabelType = source.LabelType,
        InstructionManualType = source.InstructionManualType,
        CartonType = source.CartonType,
        LogoType = source.LogoType,
        SalesPerson = source.SalesPerson,
        PurchasePerson = source.PurchasePerson,
        Remark = source.Remark
    };

    /// <summary>对同一来源中因实际采购供应商不同而分开的同 SKU 行补充说明。</summary>
    private static void AddSupplierSplitReasons(PurchaseDraftPreviewSourceGroupDto sourceGroup)
    {
        var lines = sourceGroup.Documents
            .SelectMany(document => document.Lines.Select(line => new { document.SupplierId, Line = line }))
            .ToList();
        foreach (var current in lines)
        {
            if (lines.Any(other => other.SupplierId != current.SupplierId
                && string.Equals(NormalizeText(other.Line.Sku), NormalizeText(current.Line.Sku), StringComparison.Ordinal)))
            {
                current.Line.MergeDifferenceReason = AppendReason(
                    current.Line.MergeDifferenceReason,
                    "实际采购供应商不同");
            }
        }
    }

    private static string AppendReason(string? existing, string reason) => string.IsNullOrWhiteSpace(existing)
        ? reason
        : $"{existing}；{reason}";

    private static string NormalizeText(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;

    private sealed record SelectedPurchaseDraftCandidate(
        PurchaseDraftPreviewCandidate Candidate,
        decimal PurchaseQty);

    private sealed record SourceKey(string SourceType, Guid SourceUid, string SourceNo);

    private sealed record CanonicalTarget(
        string SourceType,
        Guid SourceUid,
        Guid SupplierId,
        PurchaseDraftTargetModeEnum TargetMode,
        Guid? ExistingPurchaseOrderId,
        int? ExpectedPurchaseOrderVersion);

    private sealed record MergeKey
    {
        public MergeKey(PurchaseDraftPreviewCandidate candidate)
        {
            ProductId = candidate.ProductId;
            Sku = Normalize(candidate.Sku);
            LinkSku = Normalize(candidate.LinkSku);
            CustomerSku = Normalize(candidate.CustomerSku);
            Description = Normalize(candidate.Description);
            Color = Normalize(candidate.Color);
            Specification = Normalize(candidate.Specification);
            MaterialType = Normalize(candidate.MaterialType);
            IsCustomerSupplied = candidate.IsCustomerSupplied;
            ExecutionType = candidate.ExecutionType;
            DeliveryTargetSupplierId = candidate.DeliveryTargetSupplierId;
            Unit = Normalize(candidate.Unit);
            QuantityScale = candidate.QuantityScale;
            HasSupplierProduct = candidate.HasSupplierProduct;
            SupplierSku = Normalize(candidate.SupplierSku);
            UnitPrice = candidate.UnitPrice;
            TaxPrice = candidate.TaxPrice;
        }

        public Guid? ProductId { get; }
        public string Sku { get; }
        public string LinkSku { get; }
        public string CustomerSku { get; }
        public string Description { get; }
        public string Color { get; }
        public string Specification { get; }
        public string MaterialType { get; }
        public bool IsCustomerSupplied { get; }
        public DemandExecutionTypeEnum ExecutionType { get; }
        public Guid? DeliveryTargetSupplierId { get; }
        public string Unit { get; }
        public int QuantityScale { get; }
        public bool HasSupplierProduct { get; }
        public string SupplierSku { get; }
        public decimal? UnitPrice { get; }
        public decimal? TaxPrice { get; }

        /// <summary>使用合并键自身解释分行原因，避免 UI 复制另一套兼容性规则。</summary>
        public IEnumerable<string> DescribeDifferences(MergeKey other)
        {
            if (ProductId != other.ProductId
                || Sku != other.Sku
                || LinkSku != other.LinkSku
                || CustomerSku != other.CustomerSku
                || Description != other.Description
                || Color != other.Color
                || Specification != other.Specification
                || MaterialType != other.MaterialType
                || IsCustomerSupplied != other.IsCustomerSupplied)
            {
                yield return "产品身份或物料快照不同";
            }
            if (Unit != other.Unit) yield return "计量单位不同";
            if (DeliveryTargetSupplierId != other.DeliveryTargetSupplierId) yield return "实际送达方不同";
            if (UnitPrice != other.UnitPrice || TaxPrice != other.TaxPrice) yield return "采购价格不同";
            if (HasSupplierProduct != other.HasSupplierProduct || SupplierSku != other.SupplierSku)
                yield return "供应商产品资料不同";
            if (QuantityScale != other.QuantityScale) yield return "数量精度不同";
            if (ExecutionType != other.ExecutionType) yield return "执行方式不同";
        }

        private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}

public sealed class PurchaseDraftPreviewBuildRequest
{
    public Guid ExecutionRequestId { get; set; }
    public PurchaseDraftEntryModeEnum EntryMode { get; set; }
    public PurchaseDraftLineModeEnum LineMode { get; set; } = PurchaseDraftLineModeEnum.MergeCompatibleLines;
    public string SourceType { get; set; } = string.Empty;
    public Guid? SourceUid { get; set; }
    public IReadOnlyList<PurchaseDraftPreviewCandidate> Candidates { get; set; } = Array.Empty<PurchaseDraftPreviewCandidate>();
    public IReadOnlyList<PurchaseDraftPreviewBuildTarget> Targets { get; set; } = Array.Empty<PurchaseDraftPreviewBuildTarget>();
    public IReadOnlyList<PurchaseDraftSafeAppendCandidate> SafeAppendCandidates { get; set; } = Array.Empty<PurchaseDraftSafeAppendCandidate>();
    public IReadOnlyList<PurchaseDraftPreviewHeaderFact> Headers { get; set; } = Array.Empty<PurchaseDraftPreviewHeaderFact>();
}

/// <summary>按来源与实际采购供应商定位的新建采购单头事实。</summary>
public sealed class PurchaseDraftPreviewHeaderFact
{
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public Guid SupplierId { get; set; }
    public PurchaseDraftPreviewHeaderDto Header { get; set; } = new();
}

/// <summary>仓储事实投影；所有字段均由服务端读取，不能直接信任客户端。</summary>
public sealed class PurchaseDraftPreviewCandidate
{
    public Guid CommandItemId { get; set; }
    public Guid NetDemandId { get; set; }
    public int NetDemandVersion { get; set; }
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public string SourceNo { get; set; } = string.Empty;
    public Guid SourceLineId { get; set; }
    public int? SourceLineNumber { get; set; }
    public Guid? CustomerId { get; set; }
    public string CustomerCode { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public bool IsCustomerSupplied { get; set; }
    public bool IsPlanningFactValid { get; set; } = true;
    public DemandExecutionTypeEnum ExecutionType { get; set; }
    public decimal RemainingQty { get; set; }
    public decimal? RequestedPurchaseQty { get; set; }
    public DateTime? DemandRequiredDate { get; set; }
    public DateTime? PlannedDeliveryDate { get; set; }
    public Guid? ProductId { get; set; }
    public string Sku { get; set; } = string.Empty;
    public string LinkSku { get; set; } = string.Empty;
    public string CustomerSku { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string ImagePath { get; set; } = string.Empty;
    public string MaterialType { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public int QuantityScale { get; set; } = 4;
    public Guid? PurchaseSupplierId { get; set; }
    public bool PurchaseSupplierIsActive { get; set; } = true;
    public string PurchaseSupplierCode { get; set; } = string.Empty;
    public string PurchaseSupplierName { get; set; } = string.Empty;
    public Guid? DeliveryTargetNetDemandId { get; set; }
    public Guid? DeliveryTargetSupplierId { get; set; }
    public bool DeliveryTargetIsValid { get; set; } = true;
    public string DeliveryTargetSupplierCode { get; set; } = string.Empty;
    public string DeliveryTargetSupplierName { get; set; } = string.Empty;
    public bool HasSupplierProduct { get; set; }
    public string SupplierSku { get; set; } = string.Empty;
    public decimal? UnitPrice { get; set; }
    public decimal? TaxPrice { get; set; }
}

public sealed class PurchaseDraftPreviewBuildTarget
{
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public Guid SupplierId { get; set; }
    public PurchaseDraftTargetModeEnum TargetMode { get; set; }
    public Guid? ExistingPurchaseOrderId { get; set; }
    public int? ExpectedPurchaseOrderVersion { get; set; }
}

public sealed class PurchaseDraftSafeAppendCandidate
{
    public Guid PurchaseOrderId { get; set; }
    public int Version { get; set; }
    public string OrderNo { get; set; } = string.Empty;
    public string SourceType { get; set; } = string.Empty;
    public Guid SourceUid { get; set; }
    public Guid SupplierId { get; set; }
    public decimal UntaxedTotalAmount { get; set; }
    public decimal TaxIncludedTotalAmount { get; set; }
    public PurchaseDraftPreviewHeaderDto Header { get; set; } = new();
}

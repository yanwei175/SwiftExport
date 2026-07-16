using System.Globalization;
using SwiftExport.Contracts.DTOs.DemandPlanning.NetDemand;
using SwiftExport.Wpf.EnterpriseGrid.Exporting;

namespace SwiftExportClient.Features.DemandPlanning.NetDemand.Services;

/// <summary>
/// 将当前净需求 Trace DTO 展平为与 UI 控件树无关的 Excel 数据快照。
/// Phase 4 接入来源级 purchase-trace API 后，只替换输入 DTO，不修改导出管线。
/// </summary>
public static class NetDemandTraceExportBuilder
{
    private static readonly IReadOnlyList<GridExportColumn> Columns =
    [
        new("source-type", "来源类型", null, 0, null),
        new("source-no", "来源单号", null, 1, null),
        new("source-line", "来源行号", null, 2, null),
        new("sku", "SKU", null, 3, null),
        new("demand-role", "需求角色", null, 4, null),
        new("net-qty", "净需求数量", null, 5, "N4"),
        new("plan-revision", "计划版本", null, 6, null),
        new("plan-lifecycle", "计划状态", null, 7, null),
        new("execution-type", "执行方式", null, 8, null),
        new("purchase-supplier", "采购供应商", null, 9, null),
        new("delivery-supplier", "送达供应商", null, 10, null),
        new("planned-qty", "计划数量", null, 11, "N4"),
        new("target-type", "目标类型", null, 12, null),
        new("target-no", "采购单号", null, 13, null),
        new("target-line", "采购行号", null, 14, null),
        new("allocated-qty", "分配数量", null, 15, "N4"),
        new("demand-required-date", "原需求交期", null, 16, "yyyy-MM-dd"),
        new("planned-delivery-date", "计划交期", null, 17, "yyyy-MM-dd"),
    ];

    public static GridExportRequest CreateRequest(
        NetDemandListItemDto summary,
        IReadOnlyList<NetDemandAllocationDto> allocations,
        IReadOnlyList<NetDemandExecutionLinkDto> executionLinks)
    {
        ArgumentNullException.ThrowIfNull(summary);
        ArgumentNullException.ThrowIfNull(allocations);
        ArgumentNullException.ThrowIfNull(executionLinks);

        var rows = new List<GridExportRow>();
        var consumedLinks = new HashSet<Guid>();
        foreach (var allocation in allocations.OrderByDescending(item => item.PlanRevision).ThenBy(item => item.Sequence))
        {
            var links = executionLinks
                .Where(link => link.NetDemandAllocationId == allocation.Uid)
                .OrderBy(link => link.TargetNo, StringComparer.OrdinalIgnoreCase)
                .ThenBy(link => link.TargetLineNumber)
                .ToArray();
            if (links.Length == 0)
            {
                rows.Add(CreateRow(summary, allocation, null));
                continue;
            }

            foreach (var link in links)
            {
                consumedLinks.Add(link.Uid);
                rows.Add(CreateRow(summary, allocation, link));
            }
        }

        foreach (var orphanLink in executionLinks.Where(link => !consumedLinks.Contains(link.Uid)))
        {
            rows.Add(CreateRow(summary, null, orphanLink));
        }

        if (rows.Count == 0)
        {
            rows.Add(CreateRow(summary, null, null));
        }

        return new GridExportRequest(
            Columns,
            rows,
            new GridExportOptions
            {
                IncludeHeaders = true,
                ValueMode = GridExportValueMode.Display,
                FormulaInjectionPolicy = GridFormulaInjectionPolicy.EscapeText,
                Culture = CultureInfo.GetCultureInfo("zh-CN"),
                WorksheetName = "净需求采购追溯",
            });
    }

    private static GridExportRow CreateRow(
        NetDemandListItemDto summary,
        NetDemandAllocationDto? allocation,
        NetDemandExecutionLinkDto? link)
    {
        var plannedDate = link?.PlannedDeliveryDate ?? allocation?.PlannedDeliveryDate;
        return new GridExportRow(
        [
            Cell(summary.SourceType),
            Cell(summary.SourceNo),
            Cell(summary.SourceLineNumber),
            Cell(summary.Sku),
            Cell(summary.IsCustomerSupplied ? "客供件" : "父件"),
            Cell(summary.NetQty, summary.NetQty.ToString("N4", CultureInfo.GetCultureInfo("zh-CN"))),
            Cell(allocation?.PlanRevision),
            Cell(allocation?.LifecycleStatus.ToString()),
            Cell(allocation?.ExecutionType.ToString()),
            Cell(allocation?.PurchaseSupplierName),
            Cell(allocation?.DeliveryTargetSupplierName),
            Cell(allocation?.PlannedQty, allocation?.PlannedQty.ToString("N4", CultureInfo.GetCultureInfo("zh-CN")) ?? string.Empty),
            Cell(link?.TargetType),
            Cell(link?.TargetNo),
            Cell(link?.TargetLineNumber),
            Cell(link?.AllocatedQty, link?.AllocatedQty.ToString("N4", CultureInfo.GetCultureInfo("zh-CN")) ?? string.Empty),
            Cell(summary.DemandRequiredDate, FormatDate(summary.DemandRequiredDate)),
            Cell(plannedDate, FormatDate(plannedDate)),
        ]);
    }

    private static GridExportCell Cell(object? value, string? display = null) =>
        new(value, display ?? Convert.ToString(value, CultureInfo.GetCultureInfo("zh-CN")) ?? string.Empty);

    private static string FormatDate(DateTime? value) => value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

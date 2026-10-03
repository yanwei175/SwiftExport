using Mapster;
using SwiftExport.Contracts.DTOs.ForExcel.PackingLists;
using SwiftExport.Contracts.DTOs.Shipping.PackingLists;
using SwiftExport.Core.Interfaces.ForExcel;
namespace SwiftExport.Application.Services.Shipping.PackingLists;

/// <summary>批量检查未关联装箱行的客户产品关系；检查与事务内保存共用，不参与来源或库存编排。</summary>
public sealed class PackingListImportChecker(IForExcelSalesOrderRepository products)
{
    /// <summary>读取同客户型号候选，保留歧义和不可用原因，不修改传入编辑快照。</summary>
    public async Task<List<PackingListImportLineCheckDto>> CheckAsync(Guid customerId, IReadOnlyList<PackingListDraftLineInput> lines, CancellationToken token)
    {
        var keys = lines.Where(x => !x.IsNonStandardSnapshot).Select(x => Normalize(x.CustomerSkuSnapshot)).Where(x => x.Length > 0).Distinct().ToArray();
        var candidates = (await products.MatchProductsAsync(customerId, keys, token)).Adapt<List<PackingListImportProductCandidateDto>>();
        var result = new List<PackingListImportLineCheckDto>();
        foreach (var line in lines)
        {
            var item = new PackingListImportLineCheckDto { ClientLineId = line.Uid };
            void Issue(string code, string field, string message, bool confirmation = false, bool acknowledged = false) =>
                item.Issues.Add(new() { Code = code, Field = field, Message = message, RequiresConfirmation = confirmation, Acknowledged = acknowledged });
            if (line.Qty <= 0 || line.Qty > 99999999999999.9999m || decimal.Round(line.Qty, 4) != line.Qty)
                Issue("QuantityInvalid", "Qty", "数量须大于零，且最多四位小数。");
            if (line.PcsPerPackage <= 0 || line.NwPerPackage < 0 || line.GwPerPackage < 0 || line.CbmPerPackage < 0 || line.NwPerPackage > line.GwPerPackage)
                Issue("PackingInvalid", "PcsPerPackage", "包装数值无效；未知包装可以留空。");
            foreach (var value in new[] { line.PcsPerPackage, line.NwPerPackage, line.GwPerPackage, line.CbmPerPackage })
                if (value.HasValue && (value > 99999999999999.9999m || decimal.Round(value.Value, 4) != value))
                    Issue("PackingInvalid", "Packing", "包装数值最多四位小数，且不能超出数据库数值范围。");
            foreach (var text in new[] { (line.CustomerSkuSnapshot, 200, "CustomerSkuSnapshot"), (line.OriginalLinkSku, 200, "OriginalLinkSku"),
                (line.DescriptionEnSnapshot, 2000, "DescriptionEnSnapshot"), (line.ColorNameEnSnapshot, 100, "ColorNameEnSnapshot"),
                (line.UnitSnapshot, 100, "UnitSnapshot"), (line.CustomerPoNo, 200, "CustomerPoNo"), (line.InvoiceNo, 200, "InvoiceNo"),
                (line.Marks, 2000, "Marks"), (line.Note, 2000, "Note") })
                if (text.Item1?.Trim().Length > text.Item2) Issue("TextTooLong", text.Item3, $"文本长度不能超过 {text.Item2} 个字符。");
            if (line.IsNonStandardSnapshot)
            {
                item.MatchStatus = "NonStandard";
                if (line.ProductId.HasValue) Issue("NonStandardProduct", "ProductId", "非标行不能关联标准产品。");
                if (string.IsNullOrWhiteSpace(line.DescriptionEnSnapshot)) Issue("DescriptionRequired", "DescriptionEnSnapshot", "非标行必须填写描述。");
                if (string.IsNullOrWhiteSpace(line.UnitSnapshot)) Issue("UnitRequired", "UnitSnapshot", "非标行必须填写单位。");
            }
            else
            {
                item.Candidates = candidates.Where(x => Normalize(x.CustomerSku) == Normalize(line.CustomerSkuSnapshot)).ToList();
                var selected = line.SelectedCustomerProductId.HasValue
                    ? item.Candidates.SingleOrDefault(x => x.CustomerProductId == line.SelectedCustomerProductId)
                    : line.ProductId.HasValue ? item.Candidates.FirstOrDefault(x => x.ProductId == line.ProductId) : item.Candidates.Count == 1 ? item.Candidates[0] : null;
                if (selected == null)
                {
                    item.MatchStatus = item.Candidates.Count > 1 ? "Ambiguous" : "NotMatched";
                    Issue("ProductNotMatched", "CustomerSkuSnapshot", "请修正客户型号、明确选择候选，或标记为非标。");
                }
                else if (!selected.IsUsable || !selected.ProductId.HasValue || selected.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(selected.Sku))
                { item.MatchStatus = "Unavailable"; Issue("ProductUnavailable", "ProductId", "客户关系或产品不可用。"); }
                else
                {
                    item.MatchStatus = "Matched"; item.Product = selected;
                    if (string.IsNullOrWhiteSpace(selected.Unit)) Issue("UnitRequired", "UnitSnapshot", "系统产品缺少单位，请先维护主数据。");
                    if (!string.IsNullOrWhiteSpace(line.OriginalLinkSku) && Normalize(line.OriginalLinkSku) != Normalize(selected.LinkSku))
                        Issue("LinkSkuMismatch", "OriginalLinkSku", "Excel 中性型号与系统不同，请核对后确认。", true, line.AcceptLinkSkuMismatch);
                }
            }
            result.Add(item);
        }
        return result;
    }
    // 保留符号和内部空格，仅消除输入两端空白与大小写差异。
    private static string Normalize(string? value) => value?.Trim().ToUpperInvariant() ?? "";
}

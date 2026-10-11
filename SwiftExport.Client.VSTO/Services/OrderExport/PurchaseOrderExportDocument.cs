using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SwiftExport.Client.VSTO.Excel.Writing.OrderExport;
using SwiftExport.Contracts.DTOs.Procurement.PurchaseOrderDetailDto;

namespace SwiftExport.Client.VSTO.Services.OrderExport
{
    /// <summary>冻结采购详情为模板填充快照；不下载图片，不重新定价，也不读取主数据默认值。</summary>
    public sealed class PurchaseOrderExportDocument
    {
        public const string Title = "诗亦霖卫浴订单明细表";
        public Guid OrderId { get; }
        public string OrderNo { get; }
        public string FileNameStem { get; }
        public PurchaseOrderTemplateLayout Layout { get; }
        public IReadOnlyDictionary<string, object> HeaderValues { get; }
        /// <summary>B:K 批量填充数据；A 列图片保持空白。字符串仍以文本格式写入。</summary>
        public object[,] DetailValues { get; }
        public IReadOnlyList<string> Notes { get; }
        public IReadOnlyList<string> Warnings { get; }
        public string QuantityLabel { get; }
        public string QuantityFormula { get; }
        public string AmountFormula { get; }

        /// <summary>将详情复制到固定模板，明细按原行号排序；空金额明确提示统计不完整。</summary>
        public PurchaseOrderExportDocument(PurchaseOrderDetailDto detail)
        {
            if (detail == null || detail.PurchaseOrderId == Guid.Empty) throw new ArgumentException("采购详情缺少订单身份。");
            if (!string.IsNullOrWhiteSpace(detail.Currency) && !string.Equals(detail.Currency, "CNY", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("当前采购模板仅支持 CNY 未税价格。");
            if (detail.Lines == null || detail.Lines.Any(line => line == null)) throw new InvalidOperationException("采购详情明细不完整，不能导出。");
            var lines = detail.Lines.OrderBy(line => line.LineNumber).ToArray();
            var warnings = new List<string>();
            var missing = lines.Count(line => !line.UnitPrice.HasValue || !line.UnitAmount.HasValue);
            if (missing > 0) warnings.Add(missing + " 行缺少单价或金额，金额汇总不完整，空值未按零价填写。");
            var units = lines.Select(line => line.UnitSnapshot ?? "").Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var mixedUnits = units.Length > 1 || units.Any(string.IsNullOrWhiteSpace);
            if (lines.Length > 0 && mixedUnits) warnings.Add("存在不同或未知单位，数量合计仅为输入统计。");
            var notes = new List<string>();
            if (detail.DiscountRate.HasValue) notes.Add("优惠倍率（仅记录）：" + detail.DiscountRate.Value.ToString("0.####", CultureInfo.InvariantCulture));
            if (detail.DiscountAmount.HasValue) notes.Add("整单优惠金额（仅记录，未抵扣合计）：￥" + detail.DiscountAmount.Value.ToString("0.00", CultureInfo.InvariantCulture));
            if (!string.IsNullOrWhiteSpace(detail.DiscountRemark)) notes.Add("优惠备注：" + detail.DiscountRemark);
            notes.AddRange(warnings);
            Layout = new PurchaseOrderTemplateLayout(lines.Length, notes.Count);
            OrderId = detail.PurchaseOrderId; OrderNo = detail.OrderNo;
            FileNameStem = "采购单_" + (string.IsNullOrWhiteSpace(detail.BoNo) ? detail.OrderNo : detail.BoNo) + "_" + detail.SupplierName;
            HeaderValues = new Dictionary<string, object>
            {
                ["B2"] = detail.CustomerCode, ["B3"] = detail.CustomerPoNo,
                ["B4"] = string.IsNullOrWhiteSpace(detail.BoNo) ? detail.OrderNo : detail.BoNo, ["B5"] = detail.SalesPerson,
                ["D2"] = detail.OrderDate, ["D3"] = detail.ExpectedDate, ["D4"] = detail.ReplyDate, ["D5"] = detail.PurchasePerson,
                ["F2"] = detail.SupplierName, ["F3"] = detail.InletHoseType, ["F4"] = detail.AeratorType, ["F5"] = detail.PalletType,
                ["H2"] = detail.BoxType, ["H3"] = detail.LabelType, ["H4"] = detail.InstructionManualType, ["H5"] = detail.CartonType,
                ["J2"] = detail.LogoType, ["B6"] = detail.Remark
            };
            DetailValues = new object[lines.Length, 10];
            for (var row = 0; row < lines.Length; row++)
            {
                var line = lines[row];
                var values = new object[] { line.CustomerSku, line.LinkSku, line.SupplierSku, line.DescriptionZh, line.FinishZh,
                    line.FeaturesZh, line.UnitPrice.HasValue ? (object)(double)line.UnitPrice.Value : null,
                    (double)line.Quantity, line.UnitAmount.HasValue ? (object)(double)line.UnitAmount.Value : null, line.SubNote };
                for (var column = 0; column < values.Length; column++) DetailValues[row, column] = values[column];
            }
            Notes = notes.AsReadOnly(); Warnings = warnings.AsReadOnly();
            QuantityLabel = mixedUnits && lines.Length > 0 ? "合计（数量统计）" : "合计";
            // 零明细避免 SUM 引用自身；只有服务生成的公式允许写入 Formula。
            QuantityFormula = lines.Length == 0 ? "=0" : "=SUM(I9:I" + (Layout.TotalRow - 1) + ")";
            AmountFormula = lines.Length == 0 ? "=0" : "=IF(COUNT(J9:J" + (Layout.TotalRow - 1) + ")=0,\"\",SUM(J9:J" + (Layout.TotalRow - 1) + "))";
        }
    }
}

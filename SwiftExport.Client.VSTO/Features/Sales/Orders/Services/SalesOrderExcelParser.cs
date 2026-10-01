// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using SwiftExport.Client.VSTO.Excel.Reading;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.Common;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelFieldMappings;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Services
{
    /// <summary>按当前销售模板解析纯数组，映射来自缓存配置；不访问 COM、不匹配产品、不保存单据。</summary>
    public sealed class SalesOrderExcelParser
    {
        private readonly ExcelImportDefinition definition;
        private static readonly CultureInfo TemplateDateCulture = CultureInfo.GetCultureInfo("en-US");
        /// <summary>复用页面模型的反射目录，避免另建字段白名单。</summary>
        public SalesOrderExcelParser(ExcelImportRegistry registry)
        { definition = registry.Find("SalesOrder") ?? throw new ArgumentException("未注册销售订单导入模型。"); }

        /// <summary>解析冒号标签和拆分单元格标签；缺失值保持空白，客户身份由页面保留。</summary>
        public SalesOrderExcelParseResult ParseHeader(ExcelRangeSnapshot source, IReadOnlyList<ExcelFieldMappingDto> mappings)
        {
            var aliases = BuildAliases(mappings);
            var header = SalesOrderImportHeader.CreateForImport();
            var warnings = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var cells = new List<SalesOrderImportSourceCell>();
            var count = 0;
            for (var r = 0; r < source.RowCount; r++)
                for (var c = 0; c < source.ColumnCount; c++)
                {
                    var text = Text(source[r, c]);
                    if (text.Length == 0) continue;
                    var delimiter = text.IndexOfAny(new[] { ':', '：' });
                    var label = (delimiter < 0 ? text : text.Substring(0, delimiter)).Trim();
                    var normalized = ExcelHeaderNormalizer.Normalize(label);
                    object value = delimiter < 0 ? null : text.Substring(delimiter + 1).Trim();
                    aliases.TryGetValue(normalized, out var target);
                    if (target != null && !target.StartsWith("Header.", StringComparison.Ordinal)) target = null;
                    // 原表可保留 PO/Invoice 的标签和值在相邻单元格；不跨空格或合并区搜索猜值。
                    if ((delimiter < 0 || string.IsNullOrWhiteSpace(Text(value))) && c + 1 < source.ColumnCount)
                    {
                        var adjacent = source[r, c + 1];
                        var adjacentText = Text(adjacent);
                        if (adjacentText.IndexOfAny(new[] { ':', '：' }) < 0 && !aliases.ContainsKey(ExcelHeaderNormalizer.Normalize(adjacentText))) value = adjacent;
                    }
                    if (normalized == "SHIP TO")
                    {
                        value = string.Join("\n", Enumerable.Range(r + 1, Math.Min(2, source.RowCount - r - 1))
                            .Select(next => Text(source[next, c])).Where(part => part.Length > 0));
                        if (r + 2 >= source.RowCount) warnings.Add("Ship to 下方两格未全部包含在选区中，请核对收货地址。");
                    }
                    if (normalized == "TERMS") header.OriginalTerms = Text(value);
                    if (normalized == "UPDATEDATE" || normalized == "UPDATE DATE") header.OriginalUpdateDateText = Text(value);
                    cells.Add(Cell(source, r, c, label, target, source[r, c]));
                    if (target == null) continue;
                    if (!seen.Add(target)) throw new InvalidOperationException("单头多个标签命中同一目标：" + target + "，请调整选区或映射。");
                    if (Assign(header, target, value, source, r, warnings)) count++;
                }
            if (seen.Count == 0) throw new InvalidOperationException("选区内没有匹配的单头标签，请检查选区和销售订单字段映射。");
            foreach (var key in aliases.Values.Where(key => key.StartsWith("Header.", StringComparison.Ordinal)).Distinct().Where(key => !seen.Contains(key)))
                warnings.Add("未读取到 " + Label(key) + "，保留为空，可手工填写。");
            header.Source.SetCells(cells);
            return new SalesOrderExcelParseResult(header, null, Location(source) + "：单头解析完成，读取 " + count + " 个字段。未匹配或空白字段请手工补充。", warnings);
        }

        /// <summary>首行为表头，跳过合并表头跨度；客户型号为空立即结束，不根据 TOTAL 判断。</summary>
        public SalesOrderExcelParseResult ParseLines(ExcelRangeSnapshot source, IReadOnlyList<ExcelFieldMappingDto> mappings)
        {
            var aliases = BuildAliases(mappings);
            var columns = new Dictionary<int, string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var warnings = new List<string>();
            for (var c = 0; c < source.ColumnCount; c++)
            {
                var title = Text(source[0, c]);
                if (!aliases.TryGetValue(ExcelHeaderNormalizer.Normalize(title), out var key) || !key.StartsWith("Line.", StringComparison.Ordinal)) continue;
                if (!seen.Add(key)) throw new InvalidOperationException("明细多个列命中同一目标：" + key + "，请调整选区或映射。");
                columns.Add(c, key);
            }
            var customerColumn = columns.Where(pair => pair.Value == "Line.CustomerSku").Select(pair => (int?)pair.Key).SingleOrDefault();
            if (!customerColumn.HasValue) throw new InvalidOperationException("未找到映射到客户型号的列（Line.CustomerSku），不能确定明细结束位置。");
            if (source.HeaderRowCount >= source.RowCount) throw new InvalidOperationException("选区只有表头，请同时选择明细数据。");
            foreach (var key in aliases.Values.Where(key => key.StartsWith("Line.", StringComparison.Ordinal)).Distinct().Where(key => !seen.Contains(key)))
                warnings.Add("选区未包含 " + Label(key) + " 列，保留为空。");
            var lines = new List<SalesOrderImportLine>();
            var ending = "已到所选区域末尾";
            for (var r = source.HeaderRowCount; r < source.RowCount; r++)
            {
                var customerValue = source[r, customerColumn.Value];
                if (string.IsNullOrWhiteSpace(Text(customerValue)))
                { ending = "工作表第 " + (source.FirstRow + r) + " 行客户型号为空，已停止；其后内容未读取"; break; }
                if (customerValue is ErrorWrapper) throw new InvalidOperationException("第 " + (source.FirstRow + r) + " 行客户型号为 Excel 错误值，请先修正。");
                var line = SalesOrderImportLine.CreateForImport();
                var cells = new List<SalesOrderImportSourceCell>();
                for (var c = 0; c < source.ColumnCount; c++)
                {
                    columns.TryGetValue(c, out var key);
                    var title = Text(source[0, c]);
                    cells.Add(Cell(source, r, c, title, key, source[r, c]));
                    if (key != null) Assign(line, key, source[r, c], source, r, warnings);
                    // 这些统计只供来源核对，不参与身份、商业字段赋值或数据库保存。
                    var normalized = ExcelHeaderNormalizer.Normalize(title);
                    if (normalized == "AMOUNT (CNY)") line.OriginalAmountText = Text(source[r, c]);
                    if (normalized == "CTNS") line.OriginalCartonsText = Text(source[r, c]);
                    if (normalized == "CBMS") line.OriginalCbmText = Text(source[r, c]);
                }
                line.Source.SetCells(cells); lines.Add(line);
            }
            if (lines.Count == 0) throw new InvalidOperationException("未读取到明细，第一条数据的客户型号为空；原明细未替换。");
            return new SalesOrderExcelParseResult(null, lines, Location(source) + "：读取 " + lines.Count + " 条明细。" + ending + "。标准行尚未匹配产品，本期不能保存。", warnings);
        }

        // 缓存 DTO 会被刷新替换，解析前复制别名到稳定键；空占位、重复或不支持的启用配置不能静默使用。
        private Dictionary<string, string> BuildAliases(IReadOnlyList<ExcelFieldMappingDto> mappings)
        {
            var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var mapping in mappings)
            {
                if (!mapping.IsEnabled) continue;
                var name = ExcelHeaderNormalizer.Normalize(mapping.ExcelHeader);
                if (mapping.GroupCode != definition.GroupCode || name.Length == 0 || !definition.ContainsField(mapping.TargetFieldKey))
                    throw new InvalidOperationException("销售订单有无效的启用字段映射，请先在设置页修正。");
                if (aliases.ContainsKey(name)) throw new InvalidOperationException("字段映射存在重复表头：" + name);
                aliases.Add(name, mapping.TargetFieldKey);
            }
            if (aliases.Count == 0) throw new InvalidOperationException("尚未配置销售订单字段映射，请先通过设置页建立映射。");
            return aliases;
        }
        // 单个转换失败留空并反馈，数量／价格文字交给现有模型校验，不擅自变成零。
        private bool Assign(object model, string key, object raw, ExcelRangeSnapshot source, int row, List<string> warnings)
        {
            var field = definition.Fields.Single(item => item.FieldKey == key);
            object value = raw;
            if (value is ErrorWrapper) { warnings.Add("第 " + (source.FirstRow + row) + " 行 " + field.DisplayName + " 是 Excel 错误值，已留空。"); return false; }
            if ((field.ModelType == typeof(SalesOrderImportHeader) && definition.HeaderType.GetProperty(field.PropertyName).PropertyType == typeof(DateTime?))
                || (field.ModelType == typeof(SalesOrderImportLine) && definition.LineType.GetProperty(field.PropertyName).PropertyType == typeof(DateTime?)))
            {
                if (raw is double serial)
                {
                    try
                    {
                        // Excel 1900 系统含虚构的 1900-02-29；拒绝序号 60，早期日期补偿一日。
                        if (!source.Date1904 && serial >= 60 && serial < 61) throw new ArgumentException();
                        value = source.Date1904 ? new DateTime(1904, 1, 1).AddDays(serial)
                            : DateTime.FromOADate(serial < 60 ? serial + 1 : serial);
                    }
                    catch (ArgumentException) { warnings.Add("第 " + (source.FirstRow + row) + " 行 " + field.DisplayName + " 的日期序号无效，已留空。"); return false; }
                }
            }
            if (definition.TryAssign(model, key, value, TemplateDateCulture, out var error)) return true;
            warnings.Add("第 " + (source.FirstRow + row) + " 行：" + error + " 已留空，可手工填写。"); return false;
        }
        private string Label(string key) => definition.Fields.Single(field => field.FieldKey == key).DisplayName;
        private static string Location(ExcelRangeSnapshot source) => source.Workbook + " / " + source.Worksheet + "!" + source.Address;
        private static SalesOrderImportSourceCell Cell(ExcelRangeSnapshot source, int row, int column, string label, string key, object value)
            => new SalesOrderImportSourceCell(source.FirstRow + row, source.FirstColumn + column, label, key,
                value is ErrorWrapper ? "[Excel 错误值]" : Convert.ToString(value, CultureInfo.CurrentCulture) ?? "");
        // Value2 使用原始数值，保留现有 UI 文化；错误只作为类型提示，不输出 Excel 错误码对象。
        private static string Text(object value) => value is ErrorWrapper ? "[Excel 错误值]" : Convert.ToString(value, CultureInfo.CurrentCulture)?.Trim() ?? "";
    }
}

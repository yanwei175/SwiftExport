using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;

namespace SwiftExport.Client.VSTO.Services.ExcelImport
{
    /// <summary>单元格地址与参考名称，完全脱离 Excel COM。</summary>
    public sealed class ExcelCellSelection
    {
        public string Address { get; set; }
        public string WorksheetName { get; set; }
        public string Preview { get; set; }
    }
    /// <summary>选址和按完整配置读取的宿主边界；实现必须在 Excel STA 访问 COM。</summary>
    public interface IExcelAddressReader
    {
        /// <summary>选择一个单元格或完整合并区域；取消返回空，不改现有配置。</summary>
        Task<ExcelCellSelection> SelectCellAsync(CancellationToken token);
        /// <summary>捕获本次活动工作表后读取指定部分；结果不携带 COM 对象。</summary>
        Task<ExcelAddressSnapshot> ReadAsync(ExcelReadProfileDto profile, bool detail, CancellationToken token);
    }
    /// <summary>一个目标字段的原值及来源，错误值以问题描述呈现。</summary>
    public sealed class ExcelAddressValue
    {
        public string FieldKey { get; set; }
        public int Row { get; set; }
        public int Column { get; set; }
        public object Value { get; set; }
        public string Issue { get; set; }
    }
    /// <summary>当前活动工作表的一次纯托管快照；行分组由读取边界统一决定。</summary>
    public sealed class ExcelAddressSnapshot
    {
        public string WorkbookName { get; set; }
        public string WorksheetName { get; set; }
        public bool Date1904 { get; set; }
        public bool Detail { get; set; }
        public List<List<ExcelAddressValue>> Rows { get; set; } = new List<List<ExcelAddressValue>>();
    }
    /// <summary>客户端结构校验，不校验订单业务；不依赖后端 CanRead 代替本地字段边界。</summary>
    public static class ExcelAddressRules
    {
        /// <summary>仅装箱单的可选包装字段允许跳过跨行合并；数量、型号和销售采购保持严格读取。</summary>
        public static bool AllowMergedDetailSkip(string groupCode, string fieldKey) => groupCode == "PackingList"
            && new[] { "Line.Marks", "Line.PcsPerPackage", "Line.NwPerPackage", "Line.GwPerPackage", "Line.CbmPerPackage" }.Contains(fieldKey);
        /// <summary>解析合法的单格 A1 地址，接受绝对标记，拒绝范围和外部引用。</summary>
        public static bool TryCell(string address, out int row, out int column)
        {
            row = column = 0;
            var match = Regex.Match(address ?? "", @"\A\$?([A-Za-z]{1,3})\$?([1-9][0-9]{0,6})\z");
            if (!match.Success || !int.TryParse(match.Groups[2].Value, out row)) return false;
            foreach (var c in match.Groups[1].Value.ToUpperInvariant()) column = column * 26 + c - 'A' + 1;
            return row <= 1048576 && column <= 16384;
        }
        /// <summary>将选中的列或单格转换成标准列字母。</summary>
        public static string ColumnName(int column)
        {
            if (column < 1 || column > 16384) throw new ArgumentOutOfRangeException(nameof(column));
            var value = "";
            while (column > 0) { column--; value = (char)('A' + column % 26) + value; column /= 26; }
            return value;
        }
        /// <summary>校验字段目录、读取方向、同一起始行和结束边界，返回全部问题。</summary>
        public static IReadOnlyList<string> Validate(ExcelReadProfileDto profile, ExcelImportDefinition definition)
        {
            var issues = new List<string>();
            if (profile == null || definition == null || profile.GroupCode != definition.GroupCode)
                return new[] { "配置不属于当前导入分组。" };
            var fields = profile.Fields ?? new List<ExcelReadFieldDto>();
            if (fields.Count == 0) issues.Add("尚未配置任何字段地址。");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var starts = new HashSet<int>();
            foreach (var field in fields)
            {
                if (field == null) { issues.Add("配置包含空字段。"); continue; }
                var known = definition.Fields.FirstOrDefault(f => f.FieldKey == field.TargetFieldKey);
                if (known == null) issues.Add("客户端不认识字段：" + field.TargetFieldKey);
                if (!seen.Add(field.TargetFieldKey ?? "")) issues.Add("字段重复：" + field.TargetFieldKey);
                if (!TryCell(field.CellAddress, out var row, out var col)) issues.Add("地址无效：" + field.TargetFieldKey);
                else if (field.IsDownward) starts.Add(row);
                if (known != null && field.IsDownward != (known.ModelType == definition.LineType))
                    issues.Add(known.DisplayName + "：单头须读取一次，明细须向下读取。");
            }
            if (starts.Count > 1) issues.Add("所有明细字段必须从同一行开始。");
            if (starts.Count > 0)
            {
                if (profile.EndMode == 1)
                {
                    if (!Regex.IsMatch(profile.LastRowColumn ?? "", @"\A[A-Za-z]{1,3}\z")
                        || !TryCell((profile.LastRowColumn ?? "") + "1", out var r, out var c)) issues.Add("请输入合法的末行判断列，如 C。");
                }
                else if (profile.EndMode == 2)
                {
                    if (!TryCell(profile.SummaryCellAddress, out var row, out var col) || row <= starts.Min()) issues.Add("汇总单元格必须位于明细起始行之后。");
                }
                else issues.Add("存在明细字段，必须设置明细结束方式。");
            }
            return issues;
        }
    }
}

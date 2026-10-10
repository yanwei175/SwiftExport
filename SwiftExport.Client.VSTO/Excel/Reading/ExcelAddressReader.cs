using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Services.ExcelImport;
using SwiftExport.Contracts.DTOs.SystemDtos.ExcelReadProfiles;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace SwiftExport.Client.VSTO.Excel.Reading
{
    /// <summary>在 Excel STA 选择地址、捕获活动工作表并批量读取 Value2；不按可见单元格筛选。</summary>
    public sealed class ExcelAddressReader : IExcelAddressReader
    {
        private readonly ExcelInterop.Application application;
        private readonly IExcelDispatcher dispatcher;
        /// <summary>借用宿主，不释放共享 Application 或调度器。</summary>
        public ExcelAddressReader(ExcelInterop.Application application, IExcelDispatcher dispatcher)
        { this.application = application; this.dispatcher = dispatcher; }
        /// <inheritdoc />
        public async Task<ExcelCellSelection> SelectCellAsync(CancellationToken token)
        {
            ExcelCellSelection result = null;
            await dispatcher.InvokeAsync(() => { token.ThrowIfCancellationRequested(); result = Select(); token.ThrowIfCancellationRequested(); });
            return result;
        }
        // InputBox 的嵌套消息循环允许切换账号，调用方必须在返回后重新检查操作身份。
        private ExcelCellSelection Select()
        {
            ExcelInterop.Range range = null, merged = null, count = null;
            ExcelInterop.Worksheet sheet = null;
            ExcelInterop.Areas areas = null;
            try
            {
                range = application.InputBox("请选择一个单元格或一个完整合并单元格。", "获取配置地址", Type: 8) as ExcelInterop.Range;
                if (range == null) return null;
                areas = range.Areas;
                if (areas.Count != 1) throw new InvalidOperationException("不支持不连续选区。");
                count = range.Cells;
                if (Convert.ToDouble(count.CountLarge) > 1)
                {
                    ExcelInterop.Range first = null;
                    try
                    {
                        first = (ExcelInterop.Range)count[1, 1]; merged = first.MergeArea;
                        if (merged.get_Address(false, false) != range.get_Address(false, false)) throw new InvalidOperationException("请选择单个单元格或完整合并区域。");
                    }
                    finally { Release(first); }
                }
                sheet = range.Worksheet;
                var preview = ReadCell(sheet, "", range.Row, range.Column, false);
                return new ExcelCellSelection { Address = ExcelAddressRules.ColumnName(range.Column) + range.Row, WorksheetName = sheet.Name, Preview = preview.Issue ?? Convert.ToString(preview.Value) };
            }
            finally { Release(areas); Release(sheet); Release(count); Release(merged); Release(range); }
        }
        /// <inheritdoc />
        public async Task<ExcelAddressSnapshot> ReadAsync(ExcelReadProfileDto profile, bool detail, CancellationToken token)
        {
            ExcelAddressSnapshot result = null;
            await dispatcher.InvokeAsync(() => { token.ThrowIfCancellationRequested(); result = Read(profile, detail, token); });
            return result;
        }
        /// <summary>一次捕获活动工作表并读取活动字段；销售旧汇率地址在单元格访问前跳过。</summary>
        private ExcelAddressSnapshot Read(ExcelReadProfileDto profile, bool detail, CancellationToken token)
        {
            ExcelInterop.Worksheet sheet = null;
            ExcelInterop.Workbook workbook = null;
            object active = null;
            try
            {
                active = application.ActiveSheet; sheet = active as ExcelInterop.Worksheet;
                if (sheet == null) throw new InvalidOperationException("请先选择需要读取的 Excel 工作表，图表页不可读取。");
                workbook = (ExcelInterop.Workbook)sheet.Parent;
                var fields = profile.Fields.Where(f => f.IsDownward == detail
                    && !ExcelAddressRules.IsDeprecatedField(profile.GroupCode, f.TargetFieldKey)).ToArray();
                if (fields.Length == 0) throw new InvalidOperationException(detail ? "没有配置明细字段。" : "没有配置单头字段。");
                foreach (var f in fields)
                    if (!ExcelAddressRules.TryCell(f.CellAddress, out var r, out var c)) throw new InvalidOperationException("配置地址无效。");
                ExcelAddressRules.TryCell(fields[0].CellAddress, out var start, out var firstColumn);
                var end = start;
                if (detail)
                {
                    if (profile.EndMode == 2)
                    {
                        if (!ExcelAddressRules.TryCell(profile.SummaryCellAddress, out end, out var c)) throw new InvalidOperationException("汇总地址无效。");
                        end--;
                    }
                    else if (profile.EndMode == 1) end = LastRow(sheet, profile.LastRowColumn, start, token);
                    else throw new InvalidOperationException("请设置明细结束方式。");
                    if (end < start) throw new InvalidOperationException("指定边界内没有明细数据，原页面保持不变。");
                    if ((long)(end - start + 1) * fields.Length > 200000) throw new InvalidOperationException("读取范围超过 20 万单元格，请调整结束边界。");
                }
                var result = new ExcelAddressSnapshot { WorkbookName = workbook.Name, WorksheetName = sheet.Name, Date1904 = workbook.Date1904, Detail = detail };
                for (var row = start; row <= end; row++) result.Rows.Add(new System.Collections.Generic.List<ExcelAddressValue>());
                foreach (var field in fields)
                {
                    token.ThrowIfCancellationRequested();
                    ExcelAddressRules.TryCell(field.CellAddress, out var row, out var column);
                    if (!detail)
                    {
                        result.Rows[0].Add(ReadCell(sheet, field.TargetFieldKey, row, column, false)); continue;
                    }
                    if (row != start) throw new InvalidOperationException("明细起始行必须一致。");
                    ExcelInterop.Range range = null;
                    try
                    {
                        range = sheet.get_Range(ExcelAddressRules.ColumnName(column) + start, ExcelAddressRules.ColumnName(column) + end);
                        var value = range.Value2; var values = value as object[,];
                        var hasMerged = !(range.MergeCells is bool merged && !merged);
                        for (var i = 0; i < result.Rows.Count; i++)
                        {
                            if (i % 1000 == 0) token.ThrowIfCancellationRequested();
                            result.Rows[i].Add(hasMerged ? ReadCell(sheet, field.TargetFieldKey, start + i, column, true, ExcelAddressRules.AllowMergedDetailSkip(profile.GroupCode, field.TargetFieldKey))
                                : Cell(field.TargetFieldKey, start + i, column, values == null ? value : values[i + values.GetLowerBound(0), values.GetLowerBound(1)]));
                        }
                    }
                    finally { Release(range); }
                }
                return result;
            }
            finally { Release(workbook); Release(active); }
        }
        // 按块查找指定列最后一个非空、未合并的单元格，跳过合并汇总；包含隐藏／筛选行。
        // 只对有内容的候选查询合并状态，不依赖 Find、End 或 UsedRange 的历史设置。
        private static int LastRow(ExcelInterop.Worksheet sheet, string column, int start, CancellationToken token)
        {
            if (!ExcelAddressRules.TryCell((column ?? "") + "1", out var row, out var col)) throw new InvalidOperationException("末行判断列无效。");
            for (var bottom = 1048576; bottom >= start;)
            {
                token.ThrowIfCancellationRequested();
                var top = Math.Max(start, bottom - 16383);
                ExcelInterop.Range range = null;
                try
                {
                    range = sheet.get_Range(column + top, column + bottom);
                    object value = range.Value2;
                    var hasMerged = !(range.MergeCells is bool merged && !merged);
                    var last = ExcelAddressRules.FindLastUnmergedRow(value, top, bottom - top + 1,
                        candidate => hasMerged && IsMergedCell(sheet, candidate, col), token);
                    if (last >= top) return last;
                }
                finally { Release(range); }
                bottom = top - 1;
            }
            return start - 1;
        }
        /// <summary>仅检查末行候选是否属于合并区域，借用工作表并释放本次单格引用。</summary>
        private static bool IsMergedCell(ExcelInterop.Worksheet sheet, int row, int column)
        {
            ExcelInterop.Range cell = null;
            try
            {
                cell = sheet.get_Range(ExcelAddressRules.ColumnName(column) + row);
                return cell.MergeCells is bool merged && merged;
            }
            finally { Release(cell); }
        }
        // 单头合并区域读取左上角；指定包装字段的跨行合计只保留来源，不向各明细复制或自动分摊。
        private static ExcelAddressValue ReadCell(ExcelInterop.Worksheet sheet, string key, int row, int column, bool detail, bool allowMergedSkip = false)
        {
            ExcelInterop.Range cell = null, area = null, rows = null, first = null;
            try
            {
                cell = sheet.get_Range(ExcelAddressRules.ColumnName(column) + row);
                if (cell.MergeCells is bool merged && merged)
                {
                    area = cell.MergeArea; rows = area.Rows;
                    if (detail && rows.Count > 1 && allowMergedSkip)
                    {
                        first = sheet.get_Range(ExcelAddressRules.ColumnName(area.Column) + area.Row);
                        // Issue 阻止解析器向目标字段赋值；原合计值留在来源快照供人工核对。
                        return new ExcelAddressValue { FieldKey = key, Row = row, Column = column, Value = first.Value2,
                            Issue = "跨行合并区域 " + area.get_Address(false, false)
                                + " 的包装值不自动分配到各明细，目标字段已留空，请核对后补充。" };
                    }
                    if (detail && rows.Count > 1) throw new InvalidOperationException(key + " 在 " + area.get_Address(false, false)
                        + " 存在跨行合并，请拆分为一行一条明细后再读取。");
                    first = sheet.get_Range(ExcelAddressRules.ColumnName(area.Column) + area.Row);
                    return Cell(key, area.Row, area.Column, first.Value2);
                }
                return Cell(key, row, column, cell.Value2);
            }
            finally { Release(first); Release(rows); Release(area); Release(cell); }
        }
        // Excel 错误 VARIANT 不作为普通数字导入，原值仍保留在快照。
        private static ExcelAddressValue Cell(string key, int row, int column, object value)
        {
            var error = value is ErrorWrapper || value is int code && code >= -2146826288 && code <= -2146826240;
            return new ExcelAddressValue { FieldKey = key, Row = row, Column = column, Value = value, Issue = error ? "Excel 单元格为错误值，请检查公式或手工修正。" : null };
        }
        // 仅释放本次取得的临时 COM 引用，不 FinalRelease，不强制 GC。
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    }
}

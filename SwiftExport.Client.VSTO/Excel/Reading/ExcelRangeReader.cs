using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Hosting.Threading;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace SwiftExport.Client.VSTO.Excel.Reading
{
    /// <summary>InputBox 与 Value2 的 Excel 适配入口，全部 COM 操作在宿主 STA 完成。</summary>
    public sealed class ExcelRangeReader : IExcelRangeReader
    {
        private readonly ExcelInterop.Application application;
        private readonly IExcelDispatcher dispatcher;
        /// <summary>借用宿主 Application 和调度器，不负责释放它们。</summary>
        public ExcelRangeReader(ExcelInterop.Application application, IExcelDispatcher dispatcher)
        { this.application = application; this.dispatcher = dispatcher; }
        /// <inheritdoc />
        public async Task<ExcelRangeSnapshot> SelectAsync(bool detail)
        {
            ExcelRangeSnapshot result = null;
            await dispatcher.InvokeAsync(() => result = Read(detail));
            return result;
        }
        // 选择框有嵌套消息循环；只使用返回 Range 的工作簿，不从 ActiveWorkbook 猜来源。
        private ExcelRangeSnapshot Read(bool detail)
        {
            ExcelInterop.Range range = null, rows = null, columns = null, cells = null;
            ExcelInterop.Areas areas = null;
            ExcelInterop.Worksheet sheet = null;
            ExcelInterop.Workbook workbook = null;
            try
            {
                var selected = application.InputBox(detail ? "请选择含完整表头的连续明细区域（本模板从第 16 行开始）。" : "请选择销售订单单头区域（本模板 A11:P15）。",
                    "销售订单导入", Type: 8);
                range = selected as ExcelInterop.Range;
                if (range == null) return null; // 取消返回 false，不作为空白导入。
                areas = range.Areas;
                if (areas.Count != 1) throw new InvalidOperationException("请选择一个连续区域，不支持多选区。");
                rows = range.Rows; columns = range.Columns;
                if ((long)rows.Count * columns.Count > 200000 || columns.Count > 256)
                    throw new InvalidOperationException("选区过大，请仅选择单头或明细（最多 20 万单元格、256 列）。");
                sheet = range.Worksheet; workbook = (ExcelInterop.Workbook)sheet.Parent;
                var value = range.Value2;
                var values = value as object[,] ?? new object[,] { { value } };
                var span = 1;
                if (detail)
                {
                    cells = range.Cells;
                    for (var c = 1; c <= columns.Count; c++)
                    {
                        ExcelInterop.Range cell = null, merged = null, mergedRows = null;
                        try
                        {
                            cell = (ExcelInterop.Range)cells[1, c];
                            if (!(cell.MergeCells is bool isMerged) || !isMerged) continue;
                            merged = cell.MergeArea;
                            if (merged.Row != range.Row) throw new InvalidOperationException("选区从合并表头中间开始，请包含完整表头。");
                            mergedRows = merged.Rows; span = Math.Max(span, mergedRows.Count);
                        }
                        finally { Release(mergedRows); Release(merged); Release(cell); }
                    }
                }
                return new ExcelRangeSnapshot(values, workbook.Name, sheet.Name,
                    range.get_Address(false, false), range.Row, range.Column, workbook.Date1904, span);
            }
            finally { Release(cells); Release(workbook); Release(sheet); Release(columns); Release(rows); Release(areas); Release(range); }
        }
        // 仅释放本次取得的临时引用，不 FinalRelease 共享对象，不强制 GC。
        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    }
}

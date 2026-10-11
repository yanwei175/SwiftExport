using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Authentication;
using SwiftExport.Client.VSTO.Diagnostics;
using SwiftExport.Client.VSTO.Hosting.Threading;
using SwiftExport.Client.VSTO.Services.OrderExport;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace SwiftExport.Client.VSTO.Excel.Writing.OrderExport
{
    /// <summary>在宿主 STA 创建和保存自有采购工作簿，图片列始终留空；不关闭用户工作簿或 Excel。</summary>
    public sealed class PurchaseOrderWorkbookWriter : IPurchaseOrderWorkbookWriter
    {
        private readonly ExcelInterop.Application application;
        private readonly IExcelDispatcher dispatcher;
        private readonly PurchaseOrderTemplateService template;
        private readonly SessionService session;
        private readonly AddInLog log;

        /// <summary>借用宿主和调度器，不主动释放共享 Application。</summary>
        public PurchaseOrderWorkbookWriter(ExcelInterop.Application application, IExcelDispatcher dispatcher,
            PurchaseOrderTemplateService template, SessionService session, AddInLog log)
        { this.application = application; this.dispatcher = dispatcher; this.template = template; this.session = session; this.log = log; }

        /// <inheritdoc />
        public async Task<string> WriteAsync(PurchaseOrderExportDocument document, string outputDirectory, long sessionVersion, CancellationToken token)
        {
            string result = null;
            await dispatcher.InvokeAsync(() => result = Write(document, outputDirectory, sessionVersion, token));
            return result;
        }

        // 格式、数据和保存属于一个 STA 批次，避免在等待中暴露半成品工作簿。
        private string Write(PurchaseOrderExportDocument document, string directory, long version, CancellationToken token)
        {
            ExcelInterop.Workbooks books = null; ExcelInterop.Workbook book = null;
            ExcelInterop.Sheets sheets = null; ExcelInterop.Worksheet sheet = null; ExcelInterop.Window previousWindow = null;
            var temporaryFile = Path.Combine(directory, ".swiftexport-" + Guid.NewGuid().ToString("N") + ".xlsx");
            bool? screenUpdating = null, eventsEnabled = null;
            try
            {
                EnsureCurrent(version, token);
                screenUpdating = application.ScreenUpdating; eventsEnabled = application.EnableEvents;
                application.ScreenUpdating = false; application.EnableEvents = false;
                previousWindow = application.ActiveWindow;
                books = application.Workbooks; book = books.Add(ExcelInterop.XlWBATemplate.xlWBATWorksheet);
                sheets = book.Worksheets; sheet = (ExcelInterop.Worksheet)sheets[1]; sheet.Name = "采购订单";
                template.CreateTemplate(sheet, document.Layout, PurchaseOrderExportDocument.Title);
                EnsureCurrent(version, token);
                Fill(sheet, document, token);
                EnsureCurrent(version, token);
                book.SaveAs(temporaryFile, ExcelInterop.XlFileFormat.xlOpenXMLWorkbook, AccessMode: ExcelInterop.XlSaveAsAccessMode.xlNoChange);
                book.Close(false); Release(book); book = null;
                EnsureCurrent(version, token);
                return PurchaseOrderExportFiles.Publish(temporaryFile, directory, document.FileNameStem, token);
            }
            finally
            {
                // 只清理由本方法创建的临时文件和工作簿；清理异常记录，但不遮蔽原失败。
                try { if (book != null) book.Close(false); } catch (COMException ex) { log.Write("PurchaseExportCloseFailure", ex); }
                try { if (File.Exists(temporaryFile)) File.Delete(temporaryFile); } catch (IOException ex) { log.Write("PurchaseExportTempCleanupFailure", ex); }
                catch (UnauthorizedAccessException ex) { log.Write("PurchaseExportTempCleanupFailure", ex); }
                try { previousWindow?.Activate(); } catch (COMException ex) { log.Write("PurchaseExportRestoreWindowFailure", ex); }
                try { if (eventsEnabled.HasValue) application.EnableEvents = eventsEnabled.Value; }
                finally
                {
                    try { if (screenUpdating.HasValue) application.ScreenUpdating = screenUpdating.Value; }
                    finally { Release(sheet); Release(sheets); Release(book); Release(books); Release(previousWindow); }
                }
            }
        }

        // 字符串先设置文本格式，数值保持数值；批量写入 B:K，A 列不读取任何图片地址。
        private static void Fill(ExcelInterop.Worksheet sheet, PurchaseOrderExportDocument document, CancellationToken token)
        {
            foreach (var cell in document.HeaderValues)
            {
                token.ThrowIfCancellationRequested();
                WithRange(sheet, cell.Key, range =>
                {
                    if (cell.Value is DateTime date) { range.NumberFormat = "yyyy/m/d"; range.Value2 = date.ToOADate(); }
                    else { range.NumberFormat = "@"; range.Value2 = cell.Value; }
                });
            }
            var layout = document.Layout;
            if (layout.DetailCount > 0)
            {
                var end = layout.TotalRow - 1;
                WithRange(sheet, "B9:G" + end, range => range.NumberFormat = "@");
                WithRange(sheet, "K9:K" + end, range => range.NumberFormat = "@");
                WithRange(sheet, "I9:I" + end, range => range.NumberFormat = "0.####");
                WithRange(sheet, "B9:K" + end, range => range.Value2 = document.DetailValues);
            }
            WithRange(sheet, "A" + layout.TotalRow, range => range.Value2 = document.QuantityLabel);
            WithRange(sheet, "I" + layout.TotalRow, range => { range.NumberFormat = "0.####"; range.Formula = document.QuantityFormula; });
            WithRange(sheet, "J" + layout.TotalRow, range => range.Formula = document.AmountFormula);
            for (var index = 0; index < document.Notes.Count; index++)
            {
                token.ThrowIfCancellationRequested(); var text = document.Notes[index];
                var row = layout.TotalRow + 1 + index;
                WithRange(sheet, "A" + row + ":K" + row, range =>
                {
                    range.Merge(); range.NumberFormat = "@"; range.Value2 = text;
                    range.RowHeight = 45; range.WrapText = true; range.HorizontalAlignment = ExcelInterop.XlHAlign.xlHAlignLeft;
                });
            }
            // 显式计算自有工作表，宿主即使处于手动计算也能保存正确的合计缓存。
            sheet.Calculate();
        }

        private void EnsureCurrent(long version, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var identity = session.Capture();
            if (identity.Version != version || identity.User == null) throw new OperationCanceledException("账号已变化，导出已停止。", token);
        }
        // 每次仅释放本方法取得的局部引用，Worksheet 的持有者仍是最外层写入批次。
        private static void WithRange(ExcelInterop.Worksheet sheet, string address, Action<ExcelInterop.Range> action)
        {
            ExcelInterop.Range range = null;
            try { range = sheet.get_Range(address); action(range); }
            finally { Release(range); }
        }
        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    }
}

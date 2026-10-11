using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using SwiftExport.Client.VSTO.Hosting.Threading;
using ExcelInterop = Microsoft.Office.Interop.Excel;

namespace SwiftExport.Client.VSTO.Excel.Writing.OrderExport
{
    /// <summary>在 Excel 主线程建立采购模板及调整格式；不查 API、不填业务数据、不保存文件。</summary>
    public sealed class PurchaseOrderTemplateService
    {
        private readonly IExcelDispatcher dispatcher;
        private const string MoneyFormat = "\"￥\"#,##0.00_);[Red](\"￥\"#,##0.00)";

        /// <summary>复用 Excel 宿主调度器，不长期持有工作表或活动窗口。</summary>
        public PurchaseOrderTemplateService(IExcelDispatcher dispatcher)
        { this.dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher)); }

        /// <summary>在空白目标区域写入固定标签并设置格式；标题由调用方提供，业务值和图片保持空白。</summary>
        public Task CreateTemplateAsync(ExcelInterop.Worksheet sheet, int detailCount, string title)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            if (string.IsNullOrWhiteSpace(title)) throw new ArgumentException("请提供采购单标题。", nameof(title));
            var layout = new PurchaseOrderTemplateLayout(detailCount);
            return dispatcher.InvokeAsync(() => CreateTemplate(sheet, layout, title));
        }

        /// <summary>供同一 Excel STA 写入批次调用，避免嵌套调度；调用方保证目标工作表为空。</summary>
        internal void CreateTemplate(ExcelInterop.Worksheet sheet, PurchaseOrderTemplateLayout layout, string title)
        {
            // 显式查公式，避免把显示为空的公式误当作空白并覆盖。
            WithRange(sheet, "A1:M" + layout.SignatureRow, range =>
            {
                ExcelInterop.Range found = null;
                try
                {
                    found = range.Find("*", Type.Missing, ExcelInterop.XlFindLookIn.xlFormulas,
                        ExcelInterop.XlLookAt.xlPart, ExcelInterop.XlSearchOrder.xlByRows,
                        ExcelInterop.XlSearchDirection.xlNext, false, false, false);
                    if (found != null) throw new InvalidOperationException("采购模板目标区域已有内容，请使用空白工作表创建模板。");
                }
                finally { Release(found); }
            });
            ApplyFormat(sheet, layout);
            WriteLabels(sheet, layout, title);
        }

        /// <summary>调整已有采购模板的格式，保留业务值、公式和图片；明细数量由调用方明确提供。</summary>
        public Task ApplyFormatAsync(ExcelInterop.Worksheet sheet, int detailCount)
        {
            if (sheet == null) throw new ArgumentNullException(nameof(sheet));
            var layout = new PurchaseOrderTemplateLayout(detailCount);
            return dispatcher.InvokeAsync(() => ApplyFormat(sheet, layout));
        }

        // 不用 CurrentRegion、Find 合计猜测订单范围；合并前先检查冲突，避免丢弃已有值。
        private static void ApplyFormat(ExcelInterop.Worksheet sheet, PurchaseOrderTemplateLayout layout)
        {
            var merges = new[] { "A1:K1", "I2:I5", "J2:K5", "B6:K6", "A" + layout.TotalRow + ":H" + layout.TotalRow };
            foreach (var address in merges) ValidateMerge(sheet, address);
            WithRange(sheet, "A1:M" + layout.SignatureRow, range =>
            {
                WithFont(range, font =>
                {
                    font.Name = "微软雅黑"; font.Size = 12; font.Bold = false;
                    font.Strikethrough = false; font.Superscript = false; font.Subscript = false;
                    font.OutlineFont = false; font.Shadow = false;
                    font.Underline = ExcelInterop.XlUnderlineStyle.xlUnderlineStyleNone;
                    font.TintAndShade = 0; font.ThemeFont = ExcelInterop.XlThemeFont.xlThemeFontNone;
                });
                SetBorders(range, false); range.RowHeight = 120;
                range.VerticalAlignment = ExcelInterop.XlVAlign.xlVAlignCenter;
                range.HorizontalAlignment = ExcelInterop.XlHAlign.xlHAlignCenter; range.WrapText = true;
            });
            Height(sheet, "A1:K1", 45); Height(sheet, "A2:K5", 26.1);
            Height(sheet, "A6:K6", 40); Height(sheet, "A7:K7", 5.1); Height(sheet, "A8:K8", 40.2);
            Height(sheet, "A" + layout.TotalRow + ":K" + layout.SignatureRow, 30);
            var widths = new[] { 19.08, 16.15, 16.15, 16.15, 18.31, 16.69, 30.15, 11.54, 9, 17.23, 19.08, 13, 13 };
            for (var i = 0; i < widths.Length; i++)
            {
                var width = widths[i];
                WithRange(sheet, ((char)('A' + i)) + "1", range => range.ColumnWidth = width);
            }
            foreach (var column in new[] { "A", "B", "I" })
                Font(sheet, column + "1:" + column + layout.SignatureRow, font => font.Bold = true);
            Font(sheet, "G1:G" + layout.SignatureRow, font => font.Size = 8);
            Font(sheet, "A1:K1", font => { font.Size = 18; font.Bold = true; });
            Font(sheet, "A8:M8", font => font.Bold = true);
            Font(sheet, "B2:B6", font => font.Bold = false);
            foreach (var column in new[] { "C", "E", "G" })
                Font(sheet, column + "2:" + column + "6", font => font.Bold = true);
            Font(sheet, "G2:G8", font => font.Size = 12);
            Font(sheet, "J2", font => { font.Size = 26; font.Bold = true; });
            foreach (var column in new[] { "H", "J" })
                WithRange(sheet, column + "9:" + column + layout.TotalRow, range => range.NumberFormat = MoneyFormat);
            WithRange(sheet, "D2:D4", range => range.NumberFormat = "yyyy/m/d");
            Align(sheet, "G1:H" + layout.SignatureRow, ExcelInterop.XlHAlign.xlHAlignLeft);
            Align(sheet, "J1:K" + layout.SignatureRow, ExcelInterop.XlHAlign.xlHAlignLeft);
            Align(sheet, "A2:M8", ExcelInterop.XlHAlign.xlHAlignCenter);
            Align(sheet, "A1:K1", ExcelInterop.XlHAlign.xlHAlignRight);
            Align(sheet, "B6:K6", ExcelInterop.XlHAlign.xlHAlignLeft);
            foreach (var address in merges) WithRange(sheet, address, range => range.Merge());
            Align(sheet, "A" + layout.TotalRow + ":H" + layout.SignatureRow, ExcelInterop.XlHAlign.xlHAlignRight);
            Align(sheet, "I" + layout.SignatureRow, ExcelInterop.XlHAlign.xlHAlignRight);
            Font(sheet, "A" + layout.TotalRow + ":J" + layout.SignatureRow, font => font.Bold = true);
            Font(sheet, "C2", font => font.Color = 0x0000FF);
            Font(sheet, "C4", font => font.Color = 0x0000FF);
            WithRange(sheet, "A8:M8", range =>
            {
                ExcelInterop.Interior fill = null;
                try
                {
                    fill = range.Interior; fill.Pattern = ExcelInterop.XlPattern.xlPatternSolid;
                    // 固定浅灰色，避免不同工作簿主题改变截图中的表头底色。
                    fill.Color = 0xF2F2F2;
                }
                finally { Release(fill); }
            });
            WithRange(sheet, "A2:K6", range => SetBorders(range, true));
            WithRange(sheet, "A8:K" + layout.TotalRow, range => SetBorders(range, true));
            ApplyPrintSettings(sheet, layout);
        }

        // 只写固定标签；标题强制文本格式，避免等号开头的标题被当成公式。
        private static void WriteLabels(ExcelInterop.Worksheet sheet, PurchaseOrderTemplateLayout layout, string title)
        {
            WithRange(sheet, "A1", range => { range.NumberFormat = "@"; range.Value2 = title; });
            var labels = new[,] { { "客户代码", "下单日期", "供应商", "内盒" },
                { "客户PO号", "期望交期", "进水管", "标贴" }, { "采购单号", "回复交期", "起泡器", "说明书" },
                { "业务员", "跟单员", "托盘", "外箱" } };
            for (var row = 0; row < 4; row++)
                for (var column = 0; column < 4; column++)
                {
                    var text = labels[row, column];
                    WithRange(sheet, ((char)('A' + column * 2)).ToString() + (row + 2), range => range.Value2 = text);
                }
            WithRange(sheet, "I2", range => range.Value2 = "商标");
            WithRange(sheet, "A6", range => range.Value2 = "特殊要求");
            WithRange(sheet, "A8:K8", range => range.Value2 = new object[,] { { "产品图片", "客户型号", "本司型号", "工厂型号", "描述", "表面处理", "配置", "单价", "数量", "金额", "备注" } });
            WithRange(sheet, "A" + layout.TotalRow, range => range.Value2 = "合计");
            WithRange(sheet, "B" + layout.SignatureRow, range => range.Value2 = "制单：");
            WithRange(sheet, "F" + layout.SignatureRow, range => range.Value2 = "审核：");
            WithRange(sheet, "I" + layout.SignatureRow, range => range.Value2 = "批准：");
        }

        // 直接提交打印设置，避免驱动延迟校验在恢复 PrintCommunication 时遮蔽实际失败位置。
        private static void ApplyPrintSettings(ExcelInterop.Worksheet sheet, PurchaseOrderTemplateLayout layout)
        {
            ExcelInterop.PageSetup page = null;
            try
            {
                page = sheet.PageSetup;
                page.PrintTitleRows = "$8:$8"; page.PrintTitleColumns = ""; page.PrintArea = layout.PrintArea;
                page.LeftHeader = ""; page.CenterHeader = ""; page.RightHeader = "";
                page.LeftFooter = ""; page.CenterFooter = "第 &P 页，共 &N 页"; page.RightFooter = "";
                const double oneMillimeter = 72.0 / 25.4;
                page.LeftMargin = oneMillimeter; page.RightMargin = oneMillimeter;
                page.TopMargin = oneMillimeter; page.BottomMargin = oneMillimeter;
                page.HeaderMargin = 0; page.FooterMargin = oneMillimeter;
                page.PrintHeadings = false; page.PrintGridlines = false;
                page.PrintComments = ExcelInterop.XlPrintLocation.xlPrintNoComments;
                // 打印精度沿用当前驱动默认值；部分驱动接受赋值却在恢复打印通信时拒绝 1200 DPI。
                // 精度不影响工作簿布局，不让驱动分辨率阻断订单导出。
                page.CenterHorizontally = true; page.CenterVertically = false;
                page.Orientation = ExcelInterop.XlPageOrientation.xlPortrait;
                page.Draft = false; page.PaperSize = ExcelInterop.XlPaperSize.xlPaperA4;
                page.FirstPageNumber = (int)ExcelInterop.Constants.xlAutomatic;
                page.Order = ExcelInterop.XlOrder.xlDownThenOver; page.BlackAndWhite = false;
                page.Zoom = false; page.FitToPagesWide = 1; page.FitToPagesTall = false;
                page.PrintErrors = ExcelInterop.XlPrintErrors.xlPrintErrorsDisplayed;
                page.OddAndEvenPagesHeaderFooter = false; page.DifferentFirstPageHeaderFooter = false;
                page.ScaleWithDocHeaderFooter = true; page.AlignMarginsHeaderFooter = true;
            }
            finally { Release(page); }
        }

        // 同范围重复应用可接受；拒绝其他合并布局和合并后将丢失的非首格值／公式。
        private static void ValidateMerge(ExcelInterop.Worksheet sheet, string address)
        {
            WithRange(sheet, address, range =>
            {
                ExcelInterop.Range cells = null;
                try
                {
                    cells = range.Cells;
                    for (var i = 1; i <= cells.Count; i++)
                    {
                        ExcelInterop.Range cell = null, merged = null;
                        try
                        {
                            cell = (ExcelInterop.Range)cells[i];
                            if (Convert.ToBoolean(cell.MergeCells))
                            {
                                merged = cell.MergeArea;
                                if (merged.get_Address(false, false) == address) return;
                                throw new InvalidOperationException("模板合并范围冲突：" + address);
                            }
                            if (i > 1 && (cell.Value2 != null || Convert.ToBoolean(cell.HasFormula)))
                                throw new InvalidOperationException("合并会覆盖已有内容，请先整理模板区域：" + address);
                        }
                        finally { Release(merged); Release(cell); }
                    }
                }
                finally { Release(cells); }
            });
        }

        private static void Height(ExcelInterop.Worksheet sheet, string address, double value)
        { WithRange(sheet, address, range => range.RowHeight = value); }
        private static void Align(ExcelInterop.Worksheet sheet, string address, ExcelInterop.XlHAlign value)
        { WithRange(sheet, address, range => range.HorizontalAlignment = value); }
        private static void Font(ExcelInterop.Worksheet sheet, string address, Action<ExcelInterop.Font> action)
        { WithRange(sheet, address, range => WithFont(range, action)); }
        // 每层只释放自己取得的 COM 引用，不释放调用方借入的工作表，也不强制 GC。
        private static void WithRange(ExcelInterop.Worksheet sheet, string address, Action<ExcelInterop.Range> action)
        {
            ExcelInterop.Range range = null;
            try { range = sheet.get_Range(address); action(range); }
            finally { Release(range); }
        }
        private static void WithFont(ExcelInterop.Range range, Action<ExcelInterop.Font> action)
        {
            ExcelInterop.Font font = null;
            try { font = range.Font; action(font); }
            finally { Release(font); }
        }
        private static void SetBorders(ExcelInterop.Range range, bool visible)
        {
            ExcelInterop.Borders borders = null;
            try
            {
                borders = range.Borders;
                borders.LineStyle = visible ? ExcelInterop.XlLineStyle.xlContinuous : ExcelInterop.XlLineStyle.xlLineStyleNone;
                if (visible) { borders.ColorIndex = 0; borders.TintAndShade = 0; borders.Weight = ExcelInterop.XlBorderWeight.xlThin; }
            }
            finally { Release(borders); }
        }
        private static void Release(object value)
        { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
    }
}

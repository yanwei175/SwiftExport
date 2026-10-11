namespace SwiftExportClient.Services.Files.Excel;

/// <summary>固定地址布局的明细结束边界。</summary>
public enum ExcelLayoutEndMode
{
    /// <summary>读至指定判断列最后一个具有非空缓存值的行，中间空行仍保留。</summary>
    LastNonEmptyColumn = 1,

    /// <summary>读至显式汇总单元格所在行的前一行，不猜测汇总文字。</summary>
    BeforeSummaryCell = 2,
}

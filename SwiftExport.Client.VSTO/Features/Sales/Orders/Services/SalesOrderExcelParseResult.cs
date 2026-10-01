// 已淘汰：旧表头别名映射链路，仅保留历史兼容；新导入使用 ExcelReadProfile／ExcelReadField 地址配置。
using System;
using System.Collections.Generic;
using System.Linq;
using SwiftExport.Client.VSTO.Features.Sales.Orders.Models;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Services
{
    /// <summary>临时解析结果，接纳前不修改页面；提示不永久阻止人工修正。</summary>
    public sealed class SalesOrderExcelParseResult
    {
        /// <summary>复制解析部分和问题列表，单头与明细解析只设置对应部分。</summary>
        public SalesOrderExcelParseResult(SalesOrderImportHeader header, IEnumerable<SalesOrderImportLine> lines, string summary, IEnumerable<string> warnings)
        { Header = header; Lines = Array.AsReadOnly((lines ?? Enumerable.Empty<SalesOrderImportLine>()).ToArray()); Summary = summary; Warnings = Array.AsReadOnly(warnings.ToArray()); }
        public SalesOrderImportHeader Header { get; }
        public IReadOnlyList<SalesOrderImportLine> Lines { get; }
        public string Summary { get; }
        public IReadOnlyList<string> Warnings { get; }
        /// <summary>显示范围、结束原因和待核对字段，不用于日志记录。</summary>
        public string Message => Summary + (Warnings.Count == 0 ? "" : "\n" + string.Join("\n", Warnings));
    }
}

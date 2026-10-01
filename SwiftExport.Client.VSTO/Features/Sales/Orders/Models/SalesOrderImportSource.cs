using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace SwiftExport.Client.VSTO.Features.Sales.Orders.Models
{
    /// <summary>记录已脱离 Excel COM 的原始单元格文字，坐标为工作表中的一基坐标。</summary>
    public sealed class SalesOrderImportSourceCell
    {
        /// <summary>创建不可变来源，字段键使用目录逻辑键而非模型属性名；空键表示尚未映射。</summary>
        public SalesOrderImportSourceCell(int row, int column, string header, string fieldKey, string text)
        {
            if (row < 1 || column < 1) throw new ArgumentOutOfRangeException(nameof(row), "来源坐标必须大于零。");
            Row = row; Column = column; Header = header ?? ""; FieldKey = fieldKey ?? ""; Text = text ?? "";
        }
        public int Row { get; }
        public int Column { get; }
        public string Header { get; }
        public string FieldKey { get; }
        public string Text { get; }
    }

    /// <summary>单头或明细的来源快照与解析反馈，不负责读取 COM 或执行字段赋值。</summary>
    public sealed class SalesOrderImportSource : ObservableObject
    {
        private IReadOnlyList<SalesOrderImportSourceCell> cells = Array.AsReadOnly(new SalesOrderImportSourceCell[0]);
        private IReadOnlyList<string> issues = Array.AsReadOnly(new string[0]);
        /// <summary>不可变原始值列表，手工修正编辑字段不会改写这些来源。</summary>
        public IReadOnlyList<SalesOrderImportSourceCell> Cells => cells;
        /// <summary>尚未解决的解析问题；后续赋值器或人工修正流程显式替换。</summary>
        public IReadOnlyList<string> Issues => issues;
        /// <summary>进入现有本地校验汇总的解析错误。</summary>
        public string Error => string.Join("；", Issues);
        /// <summary>复制解析器的来源列表，不持有外部可变集合；来源文字不写日志。</summary>
        public void SetCells(IEnumerable<SalesOrderImportSourceCell> values)
        {
            cells = Array.AsReadOnly((values ?? Enumerable.Empty<SalesOrderImportSourceCell>()).ToArray());
            OnPropertyChanged(nameof(Cells));
        }
        /// <summary>替换当前未解决问题，空集合表示问题已被明确处理，不自动隐藏错误。</summary>
        public void SetIssues(IEnumerable<string> values)
        {
            issues = Array.AsReadOnly((values ?? Enumerable.Empty<string>()).Where(v => !string.IsNullOrWhiteSpace(v)).ToArray());
            OnPropertyChanged(nameof(Issues)); OnPropertyChanged(nameof(Error));
        }
    }
}

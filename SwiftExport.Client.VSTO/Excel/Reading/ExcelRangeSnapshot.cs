using System;
using System.Threading.Tasks;

namespace SwiftExport.Client.VSTO.Excel.Reading
{
    /// <summary>脱离 COM 的选区快照；数组统一为零基，来源坐标为工作表一基坐标。</summary>
    public sealed class ExcelRangeSnapshot
    {
        private readonly object[,] values;
        /// <summary>复制选区数据及日期系统，不持有外部数组或工作簿引用。</summary>
        public ExcelRangeSnapshot(object[,] values, string workbook, string worksheet, string address,
            int firstRow, int firstColumn, bool date1904, int headerRowCount = 1)
        {
            if (values == null || values.Length == 0 || firstRow < 1 || firstColumn < 1 || headerRowCount < 1)
                throw new ArgumentException("选区快照或来源坐标无效。");
            this.values = new object[values.GetLength(0), values.GetLength(1)];
            for (var r = 0; r < RowCount; r++)
                for (var c = 0; c < ColumnCount; c++)
                    this.values[r, c] = values[r + values.GetLowerBound(0), c + values.GetLowerBound(1)];
            Workbook = workbook; Worksheet = worksheet; Address = address;
            FirstRow = firstRow; FirstColumn = firstColumn; Date1904 = date1904; HeaderRowCount = headerRowCount;
        }
        public string Workbook { get; }
        public string Worksheet { get; }
        public string Address { get; }
        public int FirstRow { get; }
        public int FirstColumn { get; }
        public bool Date1904 { get; }
        public int HeaderRowCount { get; }
        public int RowCount => values.GetLength(0);
        public int ColumnCount => values.GetLength(1);
        /// <summary>按零基坐标读取原始 Value2，不读取单元格格式化文字。</summary>
        public object this[int row, int column] => values[row, column];
    }

    /// <summary>选取连续 Excel 区域并返回纯数组，取消返回空，不修改工作簿。</summary>
    public interface IExcelRangeReader
    {
        /// <summary>在 Excel STA 显示选择框，读取后释放临时 COM 引用。</summary>
        Task<ExcelRangeSnapshot> SelectAsync(bool detail);
    }
}

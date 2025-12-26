using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Linq;

namespace SwiftExprot.Utility.ExcelHelp
{
    public static class ClipboardHelper
    {
        /// <summary>
        /// 从剪贴板读取数据并解析为紧凑的二维数组
        /// </summary>
        /// <param name="skipHeader">是否跳过第一行表头</param>
        public static string[,] GetClipboardMatrix(bool skipHeader = false)
        {
            // 必须在 STA 模式下运行，Clipboard.ContainsText 可避免无谓的读取
            if (!Clipboard.ContainsText()) return null;

            string text = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(text)) return null;

            // 1. 拆分行
            var lines = text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            int totalLines = lines.Length;

            // 如果只有表头或者没数据，直接返回
            if (totalLines == 0 || (skipHeader && totalLines == 1)) return null;

            int startRow = skipHeader ? 1 : 0;
            int rowCount = totalLines - startRow;

            // 2. 确定列数（以第一行为准）
            int colCount = lines[0].Split('\t').Length;

            // 3. 构建二维数组 [行, 列]
            string[,] matrix = new string[rowCount, colCount];

            for (int i = 0; i < rowCount; i++)
            {
                string[] cells = lines[i + startRow].Split('\t');
                for (int j = 0; j < colCount; j++)
                {
                    if (j < cells.Length)
                    {
                        matrix[i, j] = cells[j];
                    }
                }
            }
            return matrix;
        }
    }
}
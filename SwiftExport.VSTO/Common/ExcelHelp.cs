using System;
using System.IO;
using System.Text.RegularExpressions;

namespace SwiftExport.VSTO.Common.ExcelHelp
{
    public static class ExcelHelp
    {
        #region 列号转换：字母 <-> 数字

        // =================================================================
        // 🚀 统一入口：方法重载
        // =================================================================
        /// <summary>
        /// 列号转换：数字 -> 字母
        /// </summary>
        public static string ConvertColumn(int index)
        {
            return ColumnIndexToColumnLetter(index);
        }

        /// <summary>
        /// 列号转换：字母 -> 数字（非法输入返回 0）
        /// </summary>
        public static int ConvertColumn(string letter)
        {
            return ColumnLetterToColumnIndex(letter);
        }

        // =================================================================
        // 核心算法：字母转数字 (Base26 -> Base10)
        // 输入："AB" -> 输出：28
        // =================================================================
        private static int ColumnLetterToColumnIndex(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName)) return 0;

            // 强制转大写并去掉首尾空白，避免输入格式导致计算错误
            columnName = columnName.Trim().ToUpperInvariant();

            int sum = 0;

            foreach (char c in columnName)
            {
                // 非 A-Z 输入直接判定为无效，避免返回错误列号
                if (c < 'A' || c > 'Z') return 0;

                // 核心算法：
                // 1. sum * 26 相当于把之前的位数进位
                // 2. (c - 'A' + 1) 是把当前字母转成 1-26 的数字
                sum *= 26;
                sum += (c - 'A' + 1);
            }

            return sum;
        }
        /// <summary>
        /// 将数字列号转换为字母列号 (例如: 1 -> A, 27 -> AA)
        /// </summary>
        /// <param name="columnIndex">从 Target.Column 获取的列号 (1开始)</param>
        /// <returns>大写字母列号</returns>
        private static string ColumnIndexToColumnLetter(int columnIndex)
        {
            if (columnIndex <= 0) return string.Empty;

            string columnName = "";

            while (columnIndex > 0)
            {
                // 核心逻辑：因为 Excel 是 1 开始的，而 ASCII 码计算是从 0 开始的
                // 所以每次计算前先减 1
                int modulo = (columnIndex - 1) % 26;

                // 'A' 的 ASCII 码是 65。 0 -> A, 1 -> B ...
                columnName = Convert.ToChar('A' + modulo) + columnName;

                // 准备下一轮循环
                columnIndex = (columnIndex - 1) / 26;
            }

            return columnName;
        }

        #endregion


        /// <summary>
        /// 从单元格绝对地址（如 $A$1）中提取列字母
        /// </summary>
        public static string AddressToColumnLetter(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            int startIndex = input.IndexOf("$") + 1;
            // 如果没找到第一个 $，startIndex 会是 0 (因为 -1 + 1)
            // 这里为了安全，建议从 startIndex 开始找第二个 $
            if (startIndex <= 0) return string.Empty;

            int endIndex = input.IndexOf("$", startIndex);

            if (startIndex > 0 && endIndex > startIndex)
            {
                return input.Substring(startIndex, endIndex - startIndex);
            }
            else
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// 尝试解析是否为纯字母列号（如 "AA", "b"），成功则返回大写字母
        /// </summary>
        /// <param name="input">输入字符串</param>
        /// <param name="result">输出转换后的大写列号</param>
        /// <returns>是否转换成功</returns>
        public static bool TryParseColumnLetter(string input, out string result)
        {
            result = null;

            // 1. 空检查
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            // 2. 正则检查：只允许 A-Z 或 a-z
            string pattern = "^[A-Za-z]+$";
            if (Regex.IsMatch(input, pattern))
            {
                // 3. 转换成功：转为大写并输出
                result = input.ToUpper();
                return true;
            }

            // 4. 转换失败
            return false;
        }

        /// <summary>
        /// 像素转毫米
        /// </summary>
        public static double PixelsToMillimeters(int pixels, float dpi)
        {
            // 1 英寸 = 25.4 毫米
            if (dpi == 0) return 0;
            return pixels * 25.4 / dpi;
        }

        /// <summary>
        /// 毫米转像素
        /// </summary>
        public static int MillimetersToPixels(double mm, float dpi)
        {
            // 1 英寸 = 25.4 毫米
            // 使用 Math.Round 四舍五入，比直接转 int 更精确一点
            return (int)Math.Round(mm * dpi / 25.4);
        }

        /// <summary>
        /// 验证输入是否为有效的数字（允许小数）
        /// </summary>
        public static bool IsValidNumber(string input)
        {
            if (string.IsNullOrEmpty(input)) return false;
            return Regex.IsMatch(input, @"^\d*\.?\d*$");
        }

        /// <summary>
        /// 移除非法字符（只保留数字和小数点）
        /// </summary>
        public static string RemoveIllegalNumberChars(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return Regex.Replace(input, @"[^\d\.]", "");
        }

        /// <summary>
        /// 检查文件是否是图片
        /// </summary>
        public static bool IsImageFile(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return false;

            // 使用 StringComparison.OrdinalIgnoreCase 忽略大小写，不需要 ToLower()
            string ext = Path.GetExtension(filePath);

            if (string.IsNullOrEmpty(ext)) return false;

            return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }




    }
}
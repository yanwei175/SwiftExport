using System;
using System.IO;
using System.Linq;
using System.Threading;

namespace SwiftExport.Client.VSTO.Services.OrderExport
{
    /// <summary>采购导出的目录与文件名规则；通过同目录移动发布，不覆盖其他进程的既有文件。</summary>
    public static class PurchaseOrderExportFiles
    {
        /// <summary>将明确输出目录规范为绝对路径，空路径不能默认为 Excel 的工作目录。</summary>
        public static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("请填写 Excel 输出文件夹。");
            var path = directory.Trim();
            if (!Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(Path.GetPathRoot(path))
                || Path.GetPathRoot(path).Length < 3 || path.StartsWith("\\") && !path.StartsWith("\\\\"))
                throw new ArgumentException("Excel 输出文件夹必须使用完整路径。");
            return Path.GetFullPath(path);
        }

        /// <summary>清理文件名非法字符及尾部句点，限制长度，保留中文和订单编号。</summary>
        public static string SafeName(string value)
        {
            var chars = (value ?? "采购单").Select(c => char.IsControl(c) || Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray();
            var name = new string(chars).Trim().TrimEnd('.');
            if (name.Length > 120) name = name.Substring(0, 120).TrimEnd(' ', '.');
            return string.IsNullOrWhiteSpace(name) ? "采购单" : name;
        }

        /// <summary>以无覆盖 File.Move 发布完整工作簿；并发占用同名文件时继续尝试序号。</summary>
        public static string Publish(string temporaryFile, string outputDirectory, string name, CancellationToken token)
        {
            var stem = SafeName(name);
            for (var index = 0; ; index++)
            {
                token.ThrowIfCancellationRequested();
                var path = Path.Combine(outputDirectory, stem + (index == 0 ? "" : "_" + index) + ".xlsx");
                if (File.Exists(path) || Directory.Exists(path)) continue;
                try { File.Move(temporaryFile, path); return path; }
                catch (IOException) when (File.Exists(path) || Directory.Exists(path)) { /* 另一导出刚占用此名称，不能覆盖。 */ }
            }
        }
    }
}
